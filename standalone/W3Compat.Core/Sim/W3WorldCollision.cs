namespace W3Compat.Sim;

/// <summary>动态碰撞索引所需的单位最小接口。</summary>
public interface ISimCollisionUnit
{
    int UnitId { get; }
    float PosX { get; }
    float PosY { get; }
    float CollisionSize { get; }
    bool IsDead { get; }
}

/// <summary>
/// Warsmash CWorldCollision 的均匀空间哈希实现（地面单位层）。
/// </summary>
public sealed class W3WorldCollision
{
    public const float Cell = 256f;

    private readonly Dictionary<(int, int), List<ISimCollisionUnit>> _cells = new();

    public void AddUnit(ISimCollisionUnit unit)
    {
        foreach (var key in KeysForRect(RectMin(unit), RectMax(unit)))
            Bucket(key).Add(unit);
    }

    public void RemoveUnit(ISimCollisionUnit unit)
    {
        foreach (var key in KeysForRect(RectMin(unit), RectMax(unit)))
        {
            if (!_cells.TryGetValue(key, out List<ISimCollisionUnit>? bucket))
                continue;
            bucket.Remove(unit);
            if (bucket.Count == 0)
                _cells.Remove(key);
        }
    }

    public void TranslateUnit(ISimCollisionUnit unit, (float X, float Y) oldMin, (float X, float Y) oldMax)
    {
        var newKeys = KeysForRect(RectMin(unit), RectMax(unit));
        var oldKeys = KeysForRect(oldMin, oldMax);
        if (newKeys.SequenceEqual(oldKeys))
            return;
        var oldSet = new HashSet<(int, int)>(oldKeys);
        var newSet = new HashSet<(int, int)>(newKeys);
        foreach (var key in oldKeys)
        {
            if (newSet.Contains(key))
                continue;
            if (_cells.TryGetValue(key, out List<ISimCollisionUnit>? bucket))
            {
                bucket.Remove(unit);
                if (bucket.Count == 0)
                    _cells.Remove(key);
            }
        }
        foreach (var key in newKeys)
        {
            if (oldSet.Contains(key))
                continue;
            List<ISimCollisionUnit> bucket = Bucket(key);
            if (!bucket.Contains(unit))
                bucket.Add(unit);
        }
    }

    public bool IntersectsAnythingOtherThan((float X, float Y) rectMin, (float X, float Y) rectMax, ISimCollisionUnit? ignore)
    {
        foreach (var key in KeysForRect(rectMin, rectMax))
        {
            if (!_cells.TryGetValue(key, out List<ISimCollisionUnit>? bucket))
                continue;
            foreach (ISimCollisionUnit other in bucket)
            {
                if (ReferenceEquals(other, ignore) || other.IsDead)
                    continue;
                (float X, float Y) omin = RectMin(other);
                (float X, float Y) omax = RectMax(other);
                if (rectMin.X < omax.X && rectMax.X > omin.X && rectMin.Y < omax.Y && rectMax.Y > omin.Y)
                    return true;
            }
        }
        return false;
    }

    public List<ISimCollisionUnit> EnumUnitsInRange(float x, float y, float radius, ISimCollisionUnit? ignore)
    {
        var outList = new List<ISimCollisionUnit>();
        var seen = new HashSet<int>();
        var rmin = (x - radius, y - radius);
        var rmax = (x + radius, y + radius);
        float r2 = radius * radius;
        foreach (var key in KeysForRect(rmin, rmax))
        {
            if (!_cells.TryGetValue(key, out List<ISimCollisionUnit>? bucket))
                continue;
            foreach (ISimCollisionUnit other in bucket)
            {
                if (ReferenceEquals(other, ignore) || other.IsDead || seen.Contains(other.UnitId))
                    continue;
                seen.Add(other.UnitId);
                float dx = other.PosX - x;
                float dy = other.PosY - y;
                if (dx * dx + dy * dy <= r2)
                    outList.Add(other);
            }
        }
        return outList;
    }

    private List<ISimCollisionUnit> Bucket((int, int) key)
    {
        if (!_cells.TryGetValue(key, out List<ISimCollisionUnit>? bucket))
        {
            bucket = new List<ISimCollisionUnit>();
            _cells[key] = bucket;
        }
        return bucket;
    }

    private static (float X, float Y) RectMin(ISimCollisionUnit unit) =>
        (unit.PosX - unit.CollisionSize, unit.PosY - unit.CollisionSize);

    private static (float X, float Y) RectMax(ISimCollisionUnit unit) =>
        (unit.PosX + unit.CollisionSize, unit.PosY + unit.CollisionSize);

    private static List<(int, int)> KeysForRect((float X, float Y) rectMin, (float X, float Y) rectMax)
    {
        int x0 = (int)MathF.Floor(rectMin.X / Cell);
        int x1 = (int)MathF.Floor(rectMax.X / Cell);
        int y0 = (int)MathF.Floor(rectMin.Y / Cell);
        int y1 = (int)MathF.Floor(rectMax.Y / Cell);
        var keys = new List<(int, int)>();
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                keys.Add((cx, cy));
        return keys;
    }
}
