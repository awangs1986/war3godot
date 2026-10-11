using W3Editor.Contracts;

namespace W3Editor.Kernel;

/// <summary>One committed transaction: the deltas needed to undo it.</summary>
internal sealed class TransactionEntry
{
    public required string Label { get; init; }
    public required List<(IDocumentPart Part, IUndoDelta Delta)> Deltas { get; init; }
    public long Bytes => Deltas.Sum(d => (long)d.Delta.ApproximateBytes);
}

/// <summary>
/// Undo/redo over transactions.
///
/// Two properties the previous EditHistory lacked:
///  - deltas carry a ShapeFingerprint and are rejected (history cleared) when
///    the part's shape changed underneath them, instead of writing to
///    relocated indices (external review 4.11);
///  - eviction is by retained bytes, not entry count, so 200 map-wide strokes
///    cannot pin ~1.5 GB (external review 4.20).
/// </summary>
public sealed class KernelEditHistory : IEditHistory
{
    /// <summary>Retained undo payload budget. Oldest entries drop past this.</summary>
    public long ByteBudget { get; init; } = 256L * 1024 * 1024;

    /// <summary>Hard cap so pathological tiny-delta workloads still bound memory.</summary>
    public int MaxEntries { get; init; } = 500;

    private readonly List<TransactionEntry> _undo = new();
    private readonly List<TransactionEntry> _redo = new();
    private readonly IEditorLog _log;

    public KernelEditHistory(IEditorLog log) => _log = log;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;
    public long RetainedBytes => _undo.Sum(e => e.Bytes) + _redo.Sum(e => e.Bytes);

    public event Action? Changed;

    internal void Push(TransactionEntry entry)
    {
        if (entry.Deltas.Count == 0)
            return;
        _undo.Add(entry);
        _redo.Clear();
        Trim();
        Changed?.Invoke();
    }

    private void Trim()
    {
        while (_undo.Count > MaxEntries)
            _undo.RemoveAt(0);
        long total = _undo.Sum(e => e.Bytes);
        while (total > ByteBudget && _undo.Count > 1)
        {
            total -= _undo[0].Bytes;
            _undo.RemoveAt(0);
        }
    }

    public bool Undo() => Step(_undo, _redo, "undo");
    public bool Redo() => Step(_redo, _undo, "redo");

    private bool Step(List<TransactionEntry> from, List<TransactionEntry> to, string what)
    {
        if (from.Count == 0)
            return false;
        TransactionEntry entry = from[^1];

        foreach ((IDocumentPart part, IUndoDelta delta) in entry.Deltas)
        {
            if (delta.ShapeFingerprint == part.ShapeFingerprint)
                continue;
            // The map was resized / retiled / repaletted since this was recorded.
            // Applying it would scribble on unrelated cells.
            _log.Warn(
                $"History discarded: '{entry.Label}' was recorded against a different " +
                $"shape of part '{part.PartId}' (cannot {what} safely).");
            Clear();
            return false;
        }

        var inverse = new List<(IDocumentPart, IUndoDelta)>(entry.Deltas.Count);
        // Reverse order: deltas were recorded in application order.
        for (int i = entry.Deltas.Count - 1; i >= 0; i--)
        {
            (IDocumentPart part, IUndoDelta delta) = entry.Deltas[i];
            inverse.Add((part, delta.Apply(part)));
            part.MarkDirty();
        }
        inverse.Reverse();

        from.RemoveAt(from.Count - 1);
        to.Add(new TransactionEntry { Label = entry.Label, Deltas = inverse });
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}

/// <summary>Kernel transaction. Dispose without Commit rolls back.</summary>
public sealed class KernelEditTransaction : IEditTransaction
{
    private readonly MapDocument _document;
    private readonly KernelEditHistory _history;
    private readonly List<(IDocumentPart Part, IUndoDelta Delta)> _deltas = new();
    private bool _noUndo;
    private bool _finished;

    internal KernelEditTransaction(MapDocument document, KernelEditHistory history, string label)
    {
        _document = document;
        _history = history;
        Label = label;
    }

    public string Label { get; }
    public bool IsCommitted { get; private set; }

    public void RecordBefore(IDocumentPart part, IUndoDelta delta)
    {
        if (_finished)
            throw new InvalidOperationException($"Transaction '{Label}' is already finished.");
        if (_noUndo)
            return;
        if (delta.ShapeFingerprint != part.ShapeFingerprint)
            throw new InvalidOperationException(
                $"Delta for part '{part.PartId}' was stamped with a stale shape fingerprint.");
        _deltas.Add((part, delta));
    }

    public void SetNoUndo()
    {
        _noUndo = true;
        _deltas.Clear();
        // A half-recorded stack is more dangerous than none: everything before
        // this point would undo into a state the skipped edits never produced.
        _history.Clear();
    }

    public void Commit()
    {
        if (_finished)
            return;
        _finished = true;
        IsCommitted = true;
        if (!_noUndo && _deltas.Count > 0)
            _history.Push(new TransactionEntry { Label = Label, Deltas = new(_deltas) });
        _document.EndTransaction(this);
    }

    public void Rollback()
    {
        if (_finished)
            return;
        _finished = true;
        for (int i = _deltas.Count - 1; i >= 0; i--)
        {
            (IDocumentPart part, IUndoDelta delta) = _deltas[i];
            delta.Apply(part);
        }
        _deltas.Clear();
        _document.EndTransaction(this);
    }

    public void Dispose()
    {
        if (!_finished)
            Rollback();
    }
}
