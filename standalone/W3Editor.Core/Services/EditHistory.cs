using System.Buffers.Binary;
using W3Editor.Formats;

namespace W3Editor.Services;

/// <summary>A single undoable edit. Commands mutate the session directly.</summary>
public interface IEditCommand
{
    void Undo(MapSession session);
    void Redo(MapSession session);
    /// <summary>Corner-grid AABB for renderer sync; null = placements-only change.</summary>
    (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map);
    /// <summary>True when the command touched doodad/unit placements.</summary>
    bool TouchesPlacements { get; }
    bool TouchesPathing => false;
    bool TouchesRegions => false;
}

/// <summary>
/// Command-stack undo/redo. Terrain strokes are recorded as lossless per-corner
/// deltas; placement edits are recorded as snapshot commands.
/// </summary>
public sealed class EditHistory
{
    private const int MaxEntries = 200;

    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private byte[]? _pending; // packed corners at stroke start
    private byte[]? _pendingPathing;
    private IEditCommand? _lastApplied;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _pending = null;
        _pendingPathing = null;
        _lastApplied = null;
        Changed?.Invoke();
    }

    /// <summary>Records an already-applied placement (or other) command.</summary>
    public void Push(IEditCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > MaxEntries)
            _undo.RemoveAt(0);
        _redo.Clear();
        _lastApplied = command;
        Changed?.Invoke();
    }

    public void BeginStroke(W3EMap map) => _pending = TerrainStrokeCommand.PackAll(map);

    /// <summary>Commits the stroke when corners actually changed; discards no-op strokes.</summary>
    public void CommitStroke(W3EMap map)
    {
        if (_pending == null) return;
        var command = TerrainStrokeCommand.FromSnapshots(_pending, map);
        _pending = null;
        if (command != null)
            Push(command);
        else
            Changed?.Invoke();
    }

    public void BeginPathingStroke(W3PathingMap map) => _pendingPathing = (byte[])map.Cells.Clone();

    public void CommitPathingStroke(W3PathingMap map)
    {
        if (_pendingPathing == null) return;
        PathingStrokeCommand? command = PathingStrokeCommand.FromSnapshots(_pendingPathing, map);
        _pendingPathing = null;
        if (command != null)
            Push(command);
        else
            Changed?.Invoke();
    }

    public bool Undo(MapSession session)
    {
        if (_undo.Count == 0) return false;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo(session);
        _redo.Add(command);
        _lastApplied = command;
        Changed?.Invoke();
        return true;
    }

    public bool Redo(MapSession session)
    {
        if (_redo.Count == 0) return false;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Redo(session);
        _undo.Add(command);
        _lastApplied = command;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Corner-grid bounding box of the last applied command, for renderer sync.</summary>
    public (int X0, int Y0, int X1, int Y1)? LastAffectedRegion(W3EMap map) =>
        _lastApplied?.AffectedRegion(map);

    public bool LastTouchedPlacements => _lastApplied?.TouchesPlacements ?? false;
    public bool LastTouchedPathing => _lastApplied?.TouchesPathing ?? false;
    public bool LastTouchedRegions => _lastApplied?.TouchesRegions ?? false;
}

/// <summary>Pathing brush stroke: only changed WPM cells are retained.</summary>
public sealed class PathingStrokeCommand : IEditCommand
{
    private readonly int[] _indices;
    private readonly byte[] _before;
    private readonly byte[] _after;

    private PathingStrokeCommand(int[] indices, byte[] before, byte[] after)
    {
        _indices = indices;
        _before = before;
        _after = after;
    }

    public bool TouchesPlacements => false;
    public bool TouchesPathing => true;
    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map) => null;

    public static PathingStrokeCommand? FromSnapshots(byte[] beforeAll, W3PathingMap map)
    {
        int count = Math.Min(beforeAll.Length, map.Cells.Length);
        var indices = new List<int>();
        for (int i = 0; i < count; i++)
            if (beforeAll[i] != map.Cells[i])
                indices.Add(i);
        if (indices.Count == 0)
            return null;
        return new PathingStrokeCommand(
            indices.ToArray(),
            indices.Select(index => beforeAll[index]).ToArray(),
            indices.Select(index => map.Cells[index]).ToArray());
    }

    public void Undo(MapSession session) => Apply(session, _before);
    public void Redo(MapSession session) => Apply(session, _after);

    private void Apply(MapSession session, byte[] values)
    {
        W3PathingMap map = session.Pathing
            ?? throw new InvalidOperationException("Pathing command has no WPM target.");
        for (int i = 0; i < _indices.Length; i++)
            if (_indices[i] < map.Cells.Length)
                map.Cells[_indices[i]] = values[i];
        session.MarkPathingDirty();
    }
}

/// <summary>Undoable region add/delete/rename using lossless record snapshots.</summary>
public sealed class RegionsSnapshotCommand : IEditCommand
{
    private readonly W3RegionRecord[] _before;
    private readonly W3RegionRecord[] _after;

    public RegionsSnapshotCommand(IEnumerable<W3RegionRecord> before, IEnumerable<W3RegionRecord> after)
    {
        _before = before.Select(region => region.Clone()).ToArray();
        _after = after.Select(region => region.Clone()).ToArray();
    }

    public bool TouchesPlacements => false;
    public bool TouchesRegions => true;
    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map) => null;
    public void Undo(MapSession session) => Apply(session, _before);
    public void Redo(MapSession session) => Apply(session, _after);

    private static void Apply(MapSession session, IEnumerable<W3RegionRecord> snapshot)
    {
        W3RegionsFile regions = session.EnsureRegions();
        regions.Regions.Clear();
        regions.Regions.AddRange(snapshot.Select(region => region.Clone()));
        session.MarkRegionsDirty();
    }
}

/// <summary>Terrain stroke: lossless per-corner deltas.</summary>
public sealed class TerrainStrokeCommand : IEditCommand
{
    private const int CornerBytes = 16;

    private readonly int[] _indices;
    private readonly byte[] _before;
    private readonly byte[] _after;

    private TerrainStrokeCommand(int[] indices, byte[] before, byte[] after)
    {
        _indices = indices;
        _before = before;
        _after = after;
    }

    public bool TouchesPlacements => false;

    public static TerrainStrokeCommand? FromSnapshots(byte[] pending, W3EMap map)
    {
        byte[] after = PackAll(map);
        var indices = new List<int>();
        int count = Math.Min(pending.Length, after.Length) / CornerBytes;
        for (int i = 0; i < count; i++)
        {
            if (!pending.AsSpan(i * CornerBytes, CornerBytes).SequenceEqual(after.AsSpan(i * CornerBytes, CornerBytes)))
                indices.Add(i);
        }
        if (indices.Count == 0)
            return null;
        var beforeBytes = new byte[indices.Count * CornerBytes];
        var afterBytes = new byte[indices.Count * CornerBytes];
        for (int i = 0; i < indices.Count; i++)
        {
            pending.AsSpan(indices[i] * CornerBytes, CornerBytes).CopyTo(beforeBytes.AsSpan(i * CornerBytes));
            after.AsSpan(indices[i] * CornerBytes, CornerBytes).CopyTo(afterBytes.AsSpan(i * CornerBytes));
        }
        return new TerrainStrokeCommand(indices.ToArray(), beforeBytes, afterBytes);
    }

    public void Undo(MapSession session) => ApplyDelta(session.Map!, _indices, _before);
    public void Redo(MapSession session) => ApplyDelta(session.Map!, _indices, _after);

    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map)
    {
        if (_indices.Length == 0 || map.Width == 0)
            return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int index in _indices)
        {
            int x = index % map.Width, y = index / map.Width;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x + 1); y1 = Math.Max(y1, y + 1);
        }
        return (x0, y0, x1, y1);
    }

    private static void ApplyDelta(W3EMap map, int[] indices, byte[] packed)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < map.Corners.Length)
                Unpack(packed.AsSpan(i * CornerBytes), map.Corners[indices[i]]);
        }
    }

    internal static byte[] PackAll(W3EMap map)
    {
        var data = new byte[map.Corners.Length * CornerBytes];
        for (int i = 0; i < map.Corners.Length; i++)
            Pack(map.Corners[i], data.AsSpan(i * CornerBytes));
        return data;
    }

    /// <summary>
    /// Lossless editor/native encoding. War3 quantization belongs only at the
    /// War3 file boundary, never in undo history.
    /// </summary>
    private static void Pack(W3Corner c, Span<byte> output)
    {
        BinaryPrimitives.WriteSingleLittleEndian(output, c.GroundHeight);
        BinaryPrimitives.WriteSingleLittleEndian(output[4..], c.WaterHeight);
        ushort flags = 0;
        if (c.MapEdge) flags |= 1;
        if (c.Ramp) flags |= 2;
        if (c.Blight) flags |= 4;
        if (c.Water) flags |= 8;
        if (c.Boundary) flags |= 16;
        BinaryPrimitives.WriteUInt16LittleEndian(output[8..], flags);
        output[10] = checked((byte)c.GroundTexture);
        output[11] = checked((byte)c.GroundVariation);
        output[12] = checked((byte)c.CliffTexture);
        output[13] = checked((byte)c.CliffVariation);
        output[14] = checked((byte)c.LayerHeight);
        output[15] = 0;
    }

    private static void Unpack(ReadOnlySpan<byte> input, W3Corner c)
    {
        c.GroundHeight = BinaryPrimitives.ReadSingleLittleEndian(input);
        c.WaterHeight = BinaryPrimitives.ReadSingleLittleEndian(input[4..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(input[8..]);
        c.MapEdge = (flags & 1) != 0;
        c.Ramp = (flags & 2) != 0;
        c.Blight = (flags & 4) != 0;
        c.Water = (flags & 8) != 0;
        c.Boundary = (flags & 16) != 0;
        c.GroundTexture = input[10];
        c.GroundVariation = input[11];
        c.CliffTexture = input[12];
        c.CliffVariation = input[13];
        c.LayerHeight = input[14];
    }
}

/// <summary>Adds placements (place / paste). Undo removes them again.</summary>
public sealed class AddPlacementsCommand : IEditCommand
{
    private readonly List<W3Placement> _placements;
    public AddPlacementsCommand(IEnumerable<W3Placement> placements) => _placements = placements.ToList();

    public bool TouchesPlacements => true;
    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map) => null;

    public void Redo(MapSession session)
    {
        foreach (var p in _placements)
            session.ListFor(p).Add(p);
    }

    public void Undo(MapSession session)
    {
        foreach (var p in _placements)
            session.ListFor(p).Remove(p);
    }
}

/// <summary>Removes placements (delete). Undo restores at the original indices,
/// keeping war3map.doo record order byte-stable across delete→undo→save.</summary>
public sealed class RemovePlacementsCommand : IEditCommand
{
    private readonly List<(W3Placement P, int Index)> _restore;

    private RemovePlacementsCommand(List<(W3Placement P, int Index)> restore) => _restore = restore;

    /// <summary>Captures indices, removes the placements, returns the command for Push.</summary>
    public static RemovePlacementsCommand Execute(MapSession session, IEnumerable<W3Placement> placements)
    {
        // Single-pass index map instead of per-item IndexOf (O(n²) hurts on
        // Ctrl+A over tens of thousands of placements); reference-dedupe so a
        // caller passing the same instance twice can't make undo double-insert.
        var indexByRef = new Dictionary<W3Placement, int>(ReferenceEqualityComparer.Instance);
        foreach (var list in new[] { session.Doodads, session.Units })
            for (int i = 0; i < list.Count; i++)
                indexByRef.TryAdd(list[i], i);

        var restore = new List<(W3Placement P, int Index)>();
        var seen = new HashSet<W3Placement>(ReferenceEqualityComparer.Instance);
        foreach (W3Placement p in placements)
            if (seen.Add(p) && indexByRef.TryGetValue(p, out int index))
                restore.Add((p, index));
        foreach ((W3Placement p, int _) in restore)
            session.ListFor(p).Remove(p);
        return new RemovePlacementsCommand(restore);
    }

    public bool TouchesPlacements => true;
    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map) => null;

    public void Redo(MapSession session)
    {
        foreach ((W3Placement p, int _) in _restore)
            session.ListFor(p).Remove(p);
    }

    public void Undo(MapSession session)
    {
        // Re-insert lowest index first so later indices are valid again.
        foreach ((W3Placement p, int index) in _restore.OrderBy(e => e.Index))
        {
            var list = session.ListFor(p);
            list.Insert(Math.Clamp(index, 0, list.Count), p);
        }
    }
}

/// <summary>Move / rotate / scale / variation change: before+after field snapshots per placement.</summary>
public sealed class TransformPlacementsCommand : IEditCommand
{
    public readonly record struct State(float X, float Y, float Z, float Angle, float ScaleX, float ScaleY, float ScaleZ, int Variation);

    private readonly List<(W3Placement P, State Before, State After)> _entries;

    public TransformPlacementsCommand(List<(W3Placement P, State Before, State After)> entries) => _entries = entries;

    public static State Capture(W3Placement p) =>
        new(p.X, p.Y, p.Z, p.Angle, p.ScaleX, p.ScaleY, p.ScaleZ, p.Variation);

    public static void Apply(W3Placement p, State s)
    {
        p.X = s.X; p.Y = s.Y; p.Z = s.Z;
        p.Angle = s.Angle;
        p.ScaleX = s.ScaleX; p.ScaleY = s.ScaleY; p.ScaleZ = s.ScaleZ;
        p.Variation = s.Variation;
    }

    public bool IsNoOp => _entries.All(e => e.Before == e.After);
    public bool TouchesPlacements => true;
    public (int X0, int Y0, int X1, int Y1)? AffectedRegion(W3EMap map) => null;

    public void Redo(MapSession session)
    {
        foreach (var (p, _, after) in _entries)
            Apply(p, after);
    }

    public void Undo(MapSession session)
    {
        foreach (var (p, before, _) in _entries)
            Apply(p, before);
    }
}
