namespace W3Editor.Contracts;

/// <summary>
/// A scoped, undoable edit. HiveWE keeps a separate undo stack per subsystem
/// (terrain_undo / doodads_undo / units_undo / regions_undo / pathing_undo);
/// one transaction model across all parts is what lets a module be removed
/// without leaving orphaned history.
///
/// Dispose without Commit == Rollback, so `using` is safe by default.
/// </summary>
public interface IEditTransaction : IDisposable
{
    string Label { get; }
    bool IsCommitted { get; }

    /// <summary>
    /// Registers the delta that reverses a change. Either record it before
    /// mutating (command-style tools) or compute it by diffing a snapshot and
    /// record it before Commit (stroke-style tools) — the requirement is only
    /// that the delta restores the pre-transaction state and carries the part's
    /// current ShapeFingerprint.
    /// </summary>
    void RecordBefore(IDocumentPart part, IUndoDelta delta);

    /// <summary>
    /// Drops undo recording for this transaction (bulk automation / import).
    /// Also clears existing history, because a partial stack is worse than none.
    /// </summary>
    void SetNoUndo();

    void Commit();
    void Rollback();
}

/// <summary>Inverse of one change to one part.</summary>
public interface IUndoDelta
{
    /// <summary>
    /// Fingerprint of the part shape this delta was recorded against. The
    /// history rejects (and clears) deltas whose fingerprint no longer matches
    /// the live part — resize / retile / palette edits must invalidate history
    /// rather than silently write to relocated indices (external review 4.11).
    /// </summary>
    long ShapeFingerprint { get; }

    /// <summary>Approximate retained size; the history evicts by byte budget, not entry count (4.20).</summary>
    int ApproximateBytes { get; }

    /// <summary>Applies this delta and returns the delta that undoes it.</summary>
    IUndoDelta Apply(IDocumentPart part);
}

/// <summary>Undo/redo stack over transactions. Provided by the kernel as a capability.</summary>
public interface IEditHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    string? NextUndoLabel { get; }
    string? NextRedoLabel { get; }

    /// <summary>Total retained delta bytes; the stack trims oldest entries past its budget.</summary>
    long RetainedBytes { get; }

    bool Undo();
    bool Redo();
    void Clear();

    event Action? Changed;
}
