using System;
using System.Collections.Generic;
using W3Foundation.Terrain.Data;

namespace W3Foundation.RuntimeEditor;

/// <summary>
/// Stroke-level undo/redo for the runtime editor. Entries hold the before/after
/// document snapshots produced by the existing brush commit flow.
/// </summary>
public sealed class TerrainEditHistory
{
    private readonly List<(TerrainDocument Before, TerrainDocument After)> _entries = new();
    private int _cursor;

    public event Action? Changed;

    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _entries.Count;

    public void Push(TerrainDocument before, TerrainDocument after)
    {
        _entries.RemoveRange(_cursor, _entries.Count - _cursor);
        _entries.Add((before, after));
        _cursor = _entries.Count;
        Changed?.Invoke();
    }

    public TerrainDocument? Undo()
    {
        if (!CanUndo)
            return null;
        _cursor--;
        Changed?.Invoke();
        return _entries[_cursor].Before;
    }

    public TerrainDocument? Redo()
    {
        if (!CanRedo)
            return null;
        TerrainDocument after = _entries[_cursor].After;
        _cursor++;
        Changed?.Invoke();
        return after;
    }

    public void Clear()
    {
        _entries.Clear();
        _cursor = 0;
        Changed?.Invoke();
    }
}
