namespace W3Editor.Contracts;

/// <summary>On-disk shape of a map. Contracts-local: must not depend on W3Editor.Core.</summary>
public enum MapFormatKind
{
    None,
    War3Folder,
    War3EnvironmentFile,
    D1MapFolder,
}

/// <summary>
/// A map as a bag of parts rather than one big Map object (HiveWE keeps
/// terrain/doodads/units/regions/... as fields of a single class; that shape
/// makes features non-removable).
/// </summary>
public interface IMapDocument
{
    string Folder { get; }
    MapFormatKind Format { get; }
    bool IsDirty { get; }

    /// <summary>Typed part lookup. Returns null when the owning module is absent.</summary>
    T? GetPart<T>() where T : class, IDocumentPart;

    IDocumentPart? GetPart(string partId);
    IReadOnlyList<IDocumentPart> Parts { get; }

    /// <summary>
    /// Files present on disk that no loaded codec claimed. They are re-written
    /// verbatim on save, so removing a module never destroys its data.
    /// </summary>
    IReadOnlyList<UnknownPart> UnknownParts { get; }

    /// <summary>
    /// Opens an edit scope. All mutation must happen inside one. Throws if a
    /// transaction is already open on this document (see ThreadingContract).
    /// </summary>
    IEditTransaction BeginEdit(string label);

    /// <summary>
    /// Serializes every dirty part and commits all files in one generational
    /// transaction — either every file advances or none does (external review
    /// 4.2 / 4.3: per-file atomicity is not enough for a 3-file map).
    /// </summary>
    void Save();
}

/// <summary>One independent data section of a map, owned by exactly one module.</summary>
public interface IDocumentPart
{
    /// <summary>Stable id, e.g. "terrain" / "doodads" / "pathing".</summary>
    string PartId { get; }
    bool IsDirty { get; }

    /// <summary>
    /// Identity of the part's addressable shape (grid dimensions, palette size…).
    /// Undo deltas are stamped with this; a mismatch invalidates history instead
    /// of writing deltas to the wrong cells (external review 4.11).
    /// </summary>
    long ShapeFingerprint { get; }

    void MarkDirty();
    void ClearDirty();
}

/// <summary>A file no loaded codec claimed; preserved byte-for-byte across save.</summary>
public sealed record UnknownPart(string FileName, byte[] Bytes);
