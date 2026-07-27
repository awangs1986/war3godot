using System;
using System.Collections.Generic;
using Godot;

namespace Diablo1Like.W3.Pathing;

/// <summary>W3E 瓦片 A*（cliff/ramp 步进规则）的 Godot 桥。
/// 直接 duck-type 调用 sampler 的 is_tile_walkable/can_step，与旧
/// w3_grid_pathfinder.gd 行为完全一致：不要求 sampler 持有真实地图资源，
/// 回归测试里的假 sampler（仅实现这两个方法）同样可用。</summary>
[GlobalClass]
public partial class W3GridPathfinderBridge : RefCounted
{
    private GodotObject? _sampler;

    public W3GridPathfinderBridge() { }

    public W3GridPathfinderBridge(GodotObject? sampler)
    {
        BindSampler(sampler);
    }

    public void BindSampler(GodotObject? sampler)
    {
        _sampler = sampler;
    }

    /// <summary>GDS 别名：GDScript 无法调用带参构造，改用 new() + bind_sampler()。</summary>
    public void bind_sampler(GodotObject? sampler) => BindSampler(sampler);

    public Godot.Collections.Array<Vector2I> find_path(Vector2I start, Vector2I goal)
    {
        var empty = new Godot.Collections.Array<Vector2I>();
        if (_sampler == null)
            return empty;
        if (!IsTileWalkable(start) || !IsTileWalkable(goal))
            return empty;
        if (start == goal)
        {
            empty.Add(start);
            return empty;
        }

        var open = new List<Vector2I> { start };
        var came = new Dictionary<Vector2I, Vector2I>();
        var gscore = new Dictionary<Vector2I, int> { [start] = 0 };
        var fscore = new Dictionary<Vector2I, int> { [start] = H(start, goal) };
        var closed = new HashSet<Vector2I>();

        Vector2I[] dirs =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        while (open.Count > 0)
        {
            Vector2I current = PopLowest(open, fscore);
            if (current == goal)
                return Reconstruct(came, current);
            closed.Add(current);
            foreach (Vector2I d in dirs)
            {
                var nxt = new Vector2I(current.X + d.X, current.Y + d.Y);
                if (closed.Contains(nxt))
                    continue;
                if (!CanStep(current, nxt))
                    continue;
                // 对角步进：两个正交方向也必须可走。
                if (d.X != 0 && d.Y != 0)
                {
                    if (!CanStep(current, new Vector2I(current.X + d.X, current.Y)))
                        continue;
                    if (!CanStep(current, new Vector2I(current.X, current.Y + d.Y)))
                        continue;
                }
                int tentative = gscore[current] + (d.X != 0 && d.Y != 0 ? 14 : 10);
                if (!gscore.TryGetValue(nxt, out int old) || tentative < old)
                {
                    came[nxt] = current;
                    gscore[nxt] = tentative;
                    fscore[nxt] = tentative + H(nxt, goal);
                    if (!open.Contains(nxt))
                        open.Add(nxt);
                }
            }
        }
        return empty;
    }

    private bool IsTileWalkable(Vector2I tile) =>
        _sampler != null && _sampler.Call("is_tile_walkable", tile).AsBool();

    private bool CanStep(Vector2I from, Vector2I to) =>
        _sampler != null && _sampler.Call("can_step", from, to).AsBool();

    /// <summary>Octile 距离启发式（cardinal=10, diagonal=14）。</summary>
    public static int Heuristic(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return 10 * (dx + dy) - 6 * Math.Min(dx, dy);
    }

    /// <summary>GDS/回归测试别名：tools/w3_regression.gd 直接校验 _h。</summary>
    public int _h(Vector2I a, Vector2I b) => Heuristic(a, b);

    private static int H(Vector2I a, Vector2I b) => Heuristic(a, b);

    private static Vector2I PopLowest(List<Vector2I> open, Dictionary<Vector2I, int> fscore)
    {
        int bestI = 0;
        int bestF = fscore.GetValueOrDefault(open[0], 1 << 30);
        for (int i = 1; i < open.Count; i++)
        {
            int f = fscore.GetValueOrDefault(open[i], 1 << 30);
            if (f < bestF)
            {
                bestF = f;
                bestI = i;
            }
        }
        Vector2I v = open[bestI];
        open.RemoveAt(bestI);
        return v;
    }

    private static Godot.Collections.Array<Vector2I> Reconstruct(Dictionary<Vector2I, Vector2I> came, Vector2I current)
    {
        var path = new List<Vector2I> { current };
        while (came.TryGetValue(current, out Vector2I prev))
        {
            current = prev;
            path.Insert(0, current);
        }
        var arr = new Godot.Collections.Array<Vector2I>();
        foreach (Vector2I p in path)
            arr.Add(p);
        return arr;
    }
}
