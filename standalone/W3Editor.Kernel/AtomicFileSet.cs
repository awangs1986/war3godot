using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace W3Editor.Kernel;

/// <summary>
/// Commits a set of files as one generation.
///
/// Per-file atomic replace is not enough for a multi-file map: a crash between
/// two renames leaves terrain.bin from generation N beside placements.bin from
/// N-1, and both files are individually well-formed so nothing detects it
/// (external review 4.3). This writer therefore:
///
///   1. writes every payload to a temp and flushes it to disk (a rename can be
///      journaled ahead of unflushed data blocks — external review 4.14),
///   2. backs up the current version of each target,
///   3. renames the data files into place,
///   4. renames the generation marker LAST — the marker is the commit point.
///
/// A crash before step 4 leaves a marker that disagrees with the files on disk,
/// which <see cref="Verify"/> reports as a torn generation instead of opening a
/// silently inconsistent map.
/// </summary>
public static class AtomicFileSet
{
    public const string MarkerFileName = ".editor-generation.json";

    public sealed record FilePayload(string FileName, byte[] Bytes);

    public sealed record GenerationInfo(long Generation, string SavedUtc, Dictionary<string, string> Files);

    public sealed record VerifyResult(bool Ok, long Generation, IReadOnlyList<string> Problems)
    {
        public static VerifyResult NoMarker { get; } =
            new(true, 0, Array.Empty<string>());
    }

    /// <summary>Commits all payloads, or throws leaving the previous generation intact.</summary>
    public static long Commit(string folder, IReadOnlyList<FilePayload> files)
    {
        if (files.Count == 0)
            return ReadMarker(folder)?.Generation ?? 0;

        Directory.CreateDirectory(folder);
        SweepStaleTemps(folder);

        long generation = (ReadMarker(folder)?.Generation ?? 0) + 1;
        string token = $"{Environment.ProcessId}-{Guid.NewGuid():N}";

        var normalizedFiles = files.Select(file => new FilePayload(
            NormalizeRelativeFileName(file.FileName), file.Bytes)).ToList();
        if (normalizedFiles.Select(file => file.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != normalizedFiles.Count)
            throw new InvalidOperationException("Atomic file set contains duplicate target names.");

        var marker = new GenerationInfo(
            generation,
            DateTime.UtcNow.ToString("O"),
            normalizedFiles.ToDictionary(f => f.FileName, f => ShortHash(f.Bytes), StringComparer.OrdinalIgnoreCase));
        byte[] markerBytes = JsonSerializer.SerializeToUtf8Bytes(
            marker, new JsonSerializerOptions { WriteIndented = true });

        // Data files first, marker last: the marker rename is the commit point.
        var ordered = new List<FilePayload>(normalizedFiles) { new(MarkerFileName, markerBytes) };

        var temps = new List<(string Target, string Temp)>();
        var backups = new Dictionary<string, string>(StringComparer.Ordinal);
        var committed = new List<string>();
        try
        {
            foreach (var (name, bytes) in ordered)
            {
                string target = ResolvePath(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temp = target + $".tmp-{token}";
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                temps.Add((target, temp));
            }

            foreach (var (target, _) in temps)
            {
                if (!File.Exists(target))
                    continue;
                string backup = target + $".bak-{token}";
                File.Copy(target, backup, overwrite: true);
                backups[target] = backup;
            }

            foreach (var (target, temp) in temps)
            {
                File.Move(temp, target, overwrite: true);
                committed.Add(target);
            }
        }
        catch
        {
            for (int i = committed.Count - 1; i >= 0; i--)
            {
                string target = committed[i];
                if (backups.TryGetValue(target, out string? backup) && File.Exists(backup))
                {
                    File.Move(backup, target, overwrite: true);
                    backups.Remove(target);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            throw;
        }
        finally
        {
            foreach (var (_, temp) in temps)
                if (File.Exists(temp))
                    File.Delete(temp);
            foreach (string backup in backups.Values)
                if (File.Exists(backup))
                    File.Delete(backup);
        }

        return generation;
    }

    /// <summary>
    /// Checks the on-disk files against the generation marker. A folder with no
    /// marker (hand-made, or written by an older build) is reported Ok — absence
    /// of evidence is not a torn write.
    /// </summary>
    public static VerifyResult Verify(string folder)
    {
        GenerationInfo? marker = ReadMarker(folder);
        if (marker == null)
            return VerifyResult.NoMarker;

        var problems = new List<string>();
        foreach ((string name, string expected) in marker.Files)
        {
            string path;
            try { path = ResolvePath(folder, name); }
            catch (InvalidDataException ex)
            {
                problems.Add($"{name}: invalid path in generation marker ({ex.Message})");
                continue;
            }
            if (!File.Exists(path))
            {
                problems.Add($"{name}: recorded in generation {marker.Generation} but missing on disk");
                continue;
            }
            string actual = ShortHash(File.ReadAllBytes(path));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                problems.Add(
                    $"{name}: content does not match generation {marker.Generation} " +
                    "(torn save, or edited outside the editor)");
        }
        return new VerifyResult(problems.Count == 0, marker.Generation, problems);
    }

    public static GenerationInfo? ReadMarker(string folder)
    {
        string path = Path.Combine(folder, MarkerFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<GenerationInfo>(File.ReadAllBytes(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes .tmp-*/.bak-* left by a crashed save in a previous run.</summary>
    public static void SweepStaleTemps(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (string path in Directory.EnumerateFiles(folder, "*.tmp-*", SearchOption.AllDirectories))
            TryDelete(path);
        foreach (string path in Directory.EnumerateFiles(folder, "*.bak-*", SearchOption.AllDirectories))
            TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* another process holds it; harmless litter */ }
        catch (UnauthorizedAccessException) { }
    }

    private static string ShortHash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 8));

    /// <summary>True for files the kernel owns and codecs must never see.</summary>
    public static bool IsKernelFile(string fileName) =>
        Path.GetFileName(fileName).Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase)
        || fileName.Contains(".tmp-", StringComparison.Ordinal)
        || fileName.Contains(".bak-", StringComparison.Ordinal);

    public static string NormalizeRelativeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.IsPathRooted(fileName)
            || fileName.Contains(':'))
            throw new InvalidDataException($"invalid relative file name '{fileName}'");
        string[] segments = fileName.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException($"invalid relative file name '{fileName}'");
        return string.Join('/', segments);
    }

    public static string ResolvePath(string folder, string fileName)
    {
        string relative = NormalizeRelativeFileName(fileName);
        string root = Path.GetFullPath(folder);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"file escapes map folder: '{fileName}'");
        return path;
    }
}
