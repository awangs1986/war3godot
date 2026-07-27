using W3Editor.Contracts;

namespace W3Editor.Kernel;

/// <summary>
/// Parts container. Loading and saving are driven entirely by registered
/// codecs: whatever no codec claims is carried as an <see cref="UnknownPart"/>
/// and re-emitted byte-for-byte, which is what makes removing a module safe
/// for data.
/// </summary>
public sealed class MapDocument : IAuthoritativeMapDocument
{
    private readonly List<IDocumentPart> _parts = new();
    private readonly List<UnknownPart> _unknown = new();
    private readonly Dictionary<string, IPartCodec> _codecs = new(StringComparer.Ordinal);
    private readonly IEditorLog _log;
    private KernelEditTransaction? _open;

    public MapDocument(string folder, MapFormatKind format, IEditorLog log, KernelEditHistory history)
    {
        Folder = folder;
        Format = format;
        _log = log;
        History = history;
    }

    public string Folder { get; }
    public MapFormatKind Format { get; }
    public KernelEditHistory History { get; }
    public long Generation { get; private set; }

    public bool IsDirty => _parts.Any(p => p.IsDirty);
    public IReadOnlyList<IDocumentPart> Parts => _parts;
    public IReadOnlyList<UnknownPart> UnknownParts => _unknown;

    public T? GetPart<T>() where T : class, IDocumentPart => _parts.OfType<T>().FirstOrDefault();

    public IDocumentPart? GetPart(string partId) =>
        _parts.FirstOrDefault(p => string.Equals(p.PartId, partId, StringComparison.Ordinal));

    public IEditTransaction BeginEdit(string label)
    {
        if (_open is { IsCommitted: false })
            throw new InvalidOperationException(
                $"A transaction ('{_open.Label}') is already open on this document. " +
                "Transactions are single-threaded and must not nest (see ThreadingContract).");
        _open = new KernelEditTransaction(this, History, label);
        return _open;
    }

    internal void EndTransaction(KernelEditTransaction transaction)
    {
        if (ReferenceEquals(_open, transaction))
            _open = null;
    }

    // -----------------------------------------------------------------
    // Load
    // -----------------------------------------------------------------

    /// <summary>
    /// Runs every codec that handles this format, then sweeps the folder for
    /// unclaimed files. Codec order is the codec's declared Order.
    /// </summary>
    internal void Load(
        IReadOnlyList<IPartCodec> codecs,
        IReadOnlyDictionary<string, byte[]>? authoritativeFiles = null)
    {
        Dictionary<string, byte[]> overlays = NormalizePayloads(authoritativeFiles);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (IPartCodec codec in codecs.Where(c => c.CanHandle(Format)).OrderBy(c => c.Order))
        {
            foreach (string name in codec.FileNames)
                claimed.Add(AtomicFileSet.NormalizeRelativeFileName(name));

            var ctx = new PartReadContext(this, _log, overlays);
            try
            {
                IDocumentPart? part = codec.Read(ctx);
                if (part == null)
                    continue;
                if (GetPart(part.PartId) != null)
                {
                    _log.Warn($"Two codecs produced part '{part.PartId}'; keeping the first.");
                    continue;
                }
                _parts.Add(part);
                _codecs[part.PartId] = codec;
            }
            catch (Exception ex)
            {
                // One broken codec must not make the map unopenable; its files
                // stay unclaimed and are preserved verbatim on save.
                _log.Error($"Codec for part '{codec.PartId}' failed to read: {ex.Message}", ex);
                foreach (string name in codec.FileNames)
                    claimed.Remove(AtomicFileSet.NormalizeRelativeFileName(name));
            }
        }

        if (Directory.Exists(Folder))
        {
            foreach (string path in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(Folder, path).Replace('\\', '/');
                if (claimed.Contains(name) || AtomicFileSet.IsKernelFile(name))
                    continue;
                _unknown.Add(new UnknownPart(
                    name,
                    overlays.TryGetValue(name, out byte[]? live) ? live : File.ReadAllBytes(path)));
            }
        }

        foreach ((string name, byte[] bytes) in overlays)
        {
            if (claimed.Contains(name) || AtomicFileSet.IsKernelFile(name)
                || _unknown.Any(part => string.Equals(
                    part.FileName, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            _unknown.Add(new UnknownPart(name, bytes));
        }

        AtomicFileSet.VerifyResult verify = AtomicFileSet.Verify(Folder);
        Generation = verify.Generation;
        if (!verify.Ok)
        {
            foreach (string problem in verify.Problems)
                _log.Warn($"Generation check: {problem}");
        }
    }

    // -----------------------------------------------------------------
    // Save
    // -----------------------------------------------------------------

    public void Save() => SaveCore(authoritativeFiles: null);

    public void Save(IReadOnlyDictionary<string, byte[]> authoritativeFiles) =>
        SaveCore(authoritativeFiles);

    private void SaveCore(IReadOnlyDictionary<string, byte[]>? authoritativeFiles)
    {
        var payloads = new List<AtomicFileSet.FilePayload>();
        var writeCtx = new PartWriteContext(this, _log, payloads);

        // Every part is serialized before anything touches disk, so a codec
        // throwing cannot produce a half-written generation.
        foreach (IDocumentPart part in _parts)
        {
            if (!_codecs.TryGetValue(part.PartId, out IPartCodec? codec))
            {
                _log.Warn($"Part '{part.PartId}' has no codec; it will not be saved.");
                continue;
            }
            codec.Write(part, writeCtx);
        }

        // Unclaimed files ride along so a removed module's data survives.
        foreach (UnknownPart unknown in _unknown)
        {
            if (payloads.Any(p => string.Equals(p.FileName, unknown.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;
            payloads.Add(new AtomicFileSet.FilePayload(unknown.FileName, unknown.Bytes));
        }

        // The legacy GUI models remain authoritative during the modular C#
        // migration. Replace duplicate part snapshots before Commit so there
        // is exactly one on-disk generation and no post-commit overwrite.
        foreach ((string name, byte[] bytes) in NormalizePayloads(authoritativeFiles))
        {
            payloads.RemoveAll(payload => string.Equals(
                payload.FileName, name, StringComparison.OrdinalIgnoreCase));
            payloads.Add(new AtomicFileSet.FilePayload(name, bytes));
        }

        Generation = AtomicFileSet.Commit(Folder, payloads);
        foreach (IDocumentPart part in _parts)
            part.ClearDirty();
    }

    private static Dictionary<string, byte[]> NormalizePayloads(
        IReadOnlyDictionary<string, byte[]>? files)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (files == null)
            return result;
        foreach ((string name, byte[] bytes) in files)
        {
            string normalized = AtomicFileSet.NormalizeRelativeFileName(name);
            if (AtomicFileSet.IsKernelFile(normalized))
                throw new InvalidOperationException($"'{normalized}' is reserved by the kernel.");
            result[normalized] = bytes;
        }
        return result;
    }

    // -----------------------------------------------------------------

    private sealed class PartReadContext(
        MapDocument doc,
        IEditorLog log,
        IReadOnlyDictionary<string, byte[]> overlays) : IPartReadContext
    {
        public string Folder => doc.Folder;
        public MapFormatKind Format => doc.Format;
        public IEditorLog Log => log;

        public byte[]? ReadFile(string fileName)
        {
            fileName = AtomicFileSet.NormalizeRelativeFileName(fileName);
            if (overlays.TryGetValue(fileName, out byte[]? live))
                return live;
            string path = AtomicFileSet.ResolvePath(doc.Folder, fileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public bool FileExists(string fileName)
        {
            fileName = AtomicFileSet.NormalizeRelativeFileName(fileName);
            return overlays.ContainsKey(fileName)
                || File.Exists(AtomicFileSet.ResolvePath(doc.Folder, fileName));
        }

        public T? GetPart<T>() where T : class, IDocumentPart => doc.GetPart<T>();
    }

    private sealed class PartWriteContext(
        MapDocument doc, IEditorLog log, List<AtomicFileSet.FilePayload> sink) : IPartWriteContext
    {
        public string Folder => doc.Folder;
        public MapFormatKind Format => doc.Format;
        public IEditorLog Log => log;

        public void Emit(string fileName, byte[] bytes)
        {
            fileName = AtomicFileSet.NormalizeRelativeFileName(fileName);
            if (AtomicFileSet.IsKernelFile(fileName))
                throw new InvalidOperationException($"'{fileName}' is reserved by the kernel.");
            sink.RemoveAll(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            sink.Add(new AtomicFileSet.FilePayload(fileName, bytes));
        }

        public T? GetPart<T>() where T : class, IDocumentPart => doc.GetPart<T>();
    }
}
