namespace Diablo1Like.W3.Terrain;

/// <summary>W3E 瓦片级 A*（含悬崖/坡道步进规则）。</summary>
public sealed class W3GridPathfinder
{
    private readonly W3HeightSampler _sampler;

    public W3GridPathfinder(W3HeightSampler sampler) => _sampler = sampler;

    public List<(int X, int Y)> FindPath(int startX, int startY, int goalX, int goalY)
    {
        var empty = new List<(int, int)>();
        if (! _sampler.IsTileWalkable(startX, startY) || !_sampler.IsTileWalkable(goalX, goalY))
            return empty;
        if (startX == goalX && startY == goalY)
            return new List<(int, int)> { (startX, startY) };

        var open = new List<(int, int)> { (startX, startY) };
        var came = new Dictionary<(int, int), (int, int)>();
        var gscore = new Dictionary<(int, int), int> { [(startX, startY)] = 0 };
        var fscore = new Dictionary<(int, int), int> { [(startX, startY)] = H(startX, startY, goalX, goalY) };
        var closed = new HashSet<(int, int)>();

        (int dx, int dy)[] dirs =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
        ];

        while (open.Count > 0)
        {
            var current = PopLowest(open, fscore);
            if (current == (goalX, goalY))
                return Reconstruct(came, current);
            closed.Add(current);
            foreach (var d in dirs)
            {
                var nxt = (current.Item1 + d.dx, current.Item2 + d.dy);
                if (closed.Contains(nxt))
                    continue;
                if (!_sampler.CanStep(current.Item1, current.Item2, nxt.Item1, nxt.Item2))
                    continue;
                if (d.dx != 0 && d.dy != 0)
                {
                    if (!_sampler.CanStep(current.Item1, current.Item2, current.Item1 + d.dx, current.Item2))
                        continue;
                    if (!_sampler.CanStep(current.Item1, current.Item2, current.Item1, current.Item2 + d.dy))
                        continue;
                }
                int tentative = gscore[current] + (d.dx != 0 && d.dy != 0 ? 14 : 10);
                if (!gscore.TryGetValue(nxt, out int old) || tentative < old)
                {
                    came[nxt] = current;
                    gscore[nxt] = tentative;
                    fscore[nxt] = tentative + H(nxt.Item1, nxt.Item2, goalX, goalY);
                    if (!open.Contains(nxt))
                        open.Add(nxt);
                }
            }
        }
        return empty;
    }

    private static int H(int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        return 10 * (dx + dy) - 6 * Math.Min(dx, dy);
    }

    private static (int, int) PopLowest(List<(int, int)> open, Dictionary<(int, int), int> fscore)
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
        var v = open[bestI];
        open.RemoveAt(bestI);
        return v;
    }

    private static List<(int, int)> Reconstruct(Dictionary<(int, int), (int, int)> came, (int, int) current)
    {
        var path = new List<(int, int)> { current };
        while (came.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Insert(0, current);
        }
        return path;
    }
}
