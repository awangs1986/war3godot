namespace W3Editor.Contracts;

/// <summary>Read side of a part codec.</summary>
public interface IPartReadContext
{
    string Folder { get; }
    MapFormatKind Format { get; }
    IEditorLog Log { get; }

    /// <summary>Reads one of the codec's declared files; null when absent.</summary>
    byte[]? ReadFile(string fileName);
    bool FileExists(string fileName);

    /// <summary>Other parts already loaded this pass (codecs are ordered by dependency).</summary>
    T? GetPart<T>() where T : class, IDocumentPart;
}

/// <summary>
/// Write side. Codecs hand back BYTES; they never touch the filesystem.
/// The kernel collects every dirty part's bytes and commits them in one
/// generational transaction, so a crash can never leave terrain.bin from
/// generation N beside placements.bin from N-1 (external review 4.3).
/// </summary>
public interface IPartWriteContext
{
    string Folder { get; }
    MapFormatKind Format { get; }
    IEditorLog Log { get; }

    /// <summary>Stages one file for the atomic commit.</summary>
    void Emit(string fileName, byte[] bytes);

    T? GetPart<T>() where T : class, IDocumentPart;
}

/// <summary>
/// Binary part reader/writer (HiveWE's per-format IO). One codec owns a set of
/// filenames; files nobody claims become <see cref="UnknownPart"/> and are
/// re-emitted verbatim, which is what makes module removal data-safe.
/// </summary>
public interface IPartCodec
{
    string PartId { get; }
    IReadOnlyList<string> FileNames { get; }

    /// <summary>Load order: lower runs first. Use to express part dependencies.</summary>
    int Order { get; }

    bool CanHandle(MapFormatKind format);

    /// <summary>Returns null when the part's files are absent (optional part).</summary>
    IDocumentPart? Read(IPartReadContext ctx);

    void Write(IDocumentPart part, IPartWriteContext ctx);
}

/// <summary>
/// Text-backed part (JASS / wts / .j). Byte-stream codecs assume binary layout
/// and lose encoding + line-ending fidelity, which matters for source files a
/// human also edits outside the editor.
/// </summary>
public interface ITextPartCodec : IPartCodec
{
    System.Text.Encoding Encoding { get; }
    /// <summary>Preserve the file's original newline style on write.</summary>
    bool PreserveLineEndings { get; }
}
