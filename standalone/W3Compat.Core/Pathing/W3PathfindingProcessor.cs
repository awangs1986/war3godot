using System.Numerics;

namespace W3Compat.Pathing;

/// <summary>Warsmash CPathfindingProcessor 同步 A*（Diablo 点击移动用）。</summary>
public sealed class W3PathfindingProcessor
{
    private static readonly (int X, int Y)[] Dirs =
    [
        (-1, 1), (0, 1), (1, 1), (1, 0),
        (1, -1), (0, -1), (-1, -1), (-1, 0),
    ];

    private readonly W3PathingGrid? _grid;
    private int _jobId;

    public W3PathfindingProcessor(W3PathingGrid? grid) => _grid = grid;

    public static bool IsCollisionSizeBetterSuitedForCorners(float collisionSize) =>
        ((2 * (int)collisionSize) / 32) % 2 == 1;

    public bool PathableBetween(float startX, float startZ, float endX, float endZ, W3MovementType movement, float collisionSize)
    {
        // 与 GDS 一致：空网格视为不可通行，不抛异常
        if (_grid == null)
            return false;
        return _grid.IsPathable(endX, endZ, movement, collisionSize)
            && _grid.IsPathable(startX, endZ, movement, collisionSize)
            && _grid.IsPathable(endX, startZ, movement, collisionSize);
    }

    /// <summary>同步寻路，返回世界 XZ 路点（Vector2.X=worldX, Vector2.Y=worldZ）。</summary>
    public List<Vector2> FindPath(
        float startX, float startZ, float goalX, float goalZ,
        W3MovementType movement = W3MovementType.Foot,
        float collisionSize = 16f,
        bool allowSmoothing = true)
    {
        var empty = new List<Vector2>();
        // 与 GDS 一致：无网格时返回空路径
        W3PathingGrid? grid = _grid;
        if (grid == null)
            return empty;
        if (MathF.Abs(startX - goalX) < 1e-6f && MathF.Abs(startZ - goalZ) < 1e-6f)
            return empty;

        _jobId++;
        int jobId = _jobId;
        float weightWalls = 1e9f;
        if (!grid.IsPathable(goalX, goalZ, movement, collisionSize))
            weightWalls = 5e2f;

        bool useCorners = IsCollisionSizeBetterSuitedForCorners(collisionSize);

        var gScore = new Dictionary<(int, int), float>();
        var fScore = new Dictionary<(int, int), float>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var cameDir = new Dictionary<(int, int), int>();
        var touched = new Dictionary<(int, int), int>();

        int startGx = MapX(startX, useCorners);
        int startGz = MapZ(startZ, useCorners);
        int goalGx = MapX(goalX, useCorners);
        int goalGz = MapZ(goalZ, useCorners);

        var goals = new List<(int, int)>();
        var most = (goalGx, goalGz);
        float bestGoalD = Vector2.Distance(NodePos(most, useCorners), new Vector2(goalX, goalZ));
        for (int j = goalGz - 1; j <= goalGz + 1; j++)
        {
            for (int i = goalGx - 1; i <= goalGx + 1; i++)
            {
                if (!InGraph(i, j, useCorners))
                    continue;
                Vector2 p = NodePos((i, j), useCorners);
                if (Vector2.Distance(p, new Vector2(goalX, goalZ)) <= bestGoalD + 0.001f)
                    goals.Add((i, j));
            }
        }
        if (goals.Count == 0)
            goals.Add(most);

        var open = new List<(int, int)>();
        var openSet = new HashSet<(int, int)>();

        int startMinX = startGx;
        int startMaxX = startGx;
        int startMinZ = startGz;
        int startMaxZ = startGz;
        Vector2 startPt = NodePos((startGx, startGz), useCorners);
        if (startX > startPt.X) startMaxX = startGx + 1;
        else if (startX < startPt.X) startMinX = startGx - 1;
        if (startZ > startPt.Y) startMaxZ = startGz + 1;
        else if (startZ < startPt.Y) startMinZ = startGz - 1;

        for (int cellX = startMinX; cellX <= startMaxX; cellX++)
        {
            for (int cellZ = startMinZ; cellZ <= startMaxZ; cellZ++)
            {
                if (!InGraph(cellX, cellZ, useCorners))
                    continue;
                var key = (cellX, cellZ);
                Touch(key, jobId, touched, gScore, fScore, cameFrom, cameDir);
                Vector2 np = NodePos(key, useCorners);
                float tent = PathableBetween(startX, startZ, np.X, np.Y, movement, collisionSize)
                    ? Vector2.Distance(np, new Vector2(startX, startZ))
                    : weightWalls;
                gScore[key] = tent;
                fScore[key] = tent + H(np, goals, useCorners);
                open.Add(key);
                openSet.Add(key);
            }
        }

        int iterations = 0;
        while (open.Count > 0)
        {
            iterations++;
            if (iterations > 20000)
                break;
            var current = PopLowest(open, fScore);
            openSet.Remove(current);
            Touch(current, jobId, touched, gScore, fScore, cameFrom, cameDir);

            if (IsGoal(current, goals))
            {
                return Reconstruct(
                    current, cameFrom, cameDir, startX, startZ, goalX, goalZ,
                    movement, collisionSize, allowSmoothing, useCorners, weightWalls);
            }

            Vector2 curPos = NodePos(current, useCorners);
            float curG = gScore.GetValueOrDefault(current, float.PositiveInfinity);
            cameDir.TryGetValue(current, out int curDir);
            bool hasCurDir = cameDir.ContainsKey(current);

            for (int di = 0; di < Dirs.Length; di++)
            {
                var d = Dirs[di];
                float nx = curPos.X + d.X * W3PathingGrid.Cell;
                float nz = curPos.Y + d.Y * W3PathingGrid.Cell;
                if (!_grid!.Contains(nx, nz) && !useCorners)
                    continue;
                var nkey = (MapX(nx, useCorners), MapZ(nz, useCorners));
                if (!InGraph(nkey.Item1, nkey.Item2, useCorners))
                    continue;
                Touch(nkey, jobId, touched, gScore, fScore, cameFrom, cameDir);
                float turnCost = hasCurDir && curDir != di ? 0.25f : 0f;
                float dirLen = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                float tentative = curG + (dirLen + turnCost) * W3PathingGrid.Cell;
                if (!PathableBetween(curPos.X, curPos.Y, nx, nz, movement, collisionSize))
                    tentative += dirLen * weightWalls;
                float oldG = gScore.GetValueOrDefault(nkey, float.PositiveInfinity);
                if (tentative < oldG)
                {
                    cameFrom[nkey] = current;
                    cameDir[nkey] = di;
                    gScore[nkey] = tentative;
                    Vector2 npos = NodePos(nkey, useCorners);
                    fScore[nkey] = tentative + H(npos, goals, useCorners);
                    if (!openSet.Contains(nkey))
                    {
                        open.Add(nkey);
                        openSet.Add(nkey);
                    }
                }
            }
        }
        return empty;
    }

    private int MapX(float worldX, bool useCorners) =>
        useCorners ? _grid!.GetCornerX(worldX) : _grid!.GetCellX(worldX);

    private int MapZ(float worldZ, bool useCorners) =>
        useCorners ? _grid!.GetCornerY(worldZ) : _grid!.GetCellY(worldZ);

    private bool InGraph(int x, int z, bool useCorners)
    {
        if (useCorners)
            return x >= 0 && z >= 0 && x <= _grid!.Width && z <= _grid.Height;
        return x >= 0 && z >= 0 && x < _grid!.Width && z < _grid.Height;
    }

    private Vector2 NodePos((int X, int Y) key, bool useCorners)
    {
        if (useCorners)
            return new Vector2(_grid!.GetWorldXFromCorner(key.X), _grid.GetWorldYFromCorner(key.Y));
        return new Vector2(_grid!.GetWorldX(key.X), _grid.GetWorldY(key.Y));
    }

    private static void Touch(
        (int, int) key, int jobId,
        Dictionary<(int, int), int> touched,
        Dictionary<(int, int), float> gScore,
        Dictionary<(int, int), float> fScore,
        Dictionary<(int, int), (int, int)> cameFrom,
        Dictionary<(int, int), int> cameDir)
    {
        if (touched.GetValueOrDefault(key, -1) == jobId)
            return;
        touched[key] = jobId;
        gScore[key] = float.PositiveInfinity;
        fScore[key] = float.PositiveInfinity;
        cameFrom.Remove(key);
        cameDir.Remove(key);
    }

    private float H(Vector2 pos, List<(int, int)> goals, bool useCorners)
    {
        float best = 0f;
        foreach (var g in goals)
            best = MathF.Max(best, Vector2.Distance(pos, NodePos(g, useCorners)));
        return best;
    }

    private static bool IsGoal((int, int) key, List<(int, int)> goals)
    {
        foreach (var g in goals)
            if (g == key)
                return true;
        return false;
    }

    private static (int, int) PopLowest(List<(int, int)> open, Dictionary<(int, int), float> fScore)
    {
        int bi = 0;
        float best = fScore.GetValueOrDefault(open[0], float.PositiveInfinity);
        for (int i = 1; i < open.Count; i++)
        {
            float f = fScore.GetValueOrDefault(open[i], float.PositiveInfinity);
            if (f < best)
            {
                best = f;
                bi = i;
            }
        }
        var node = open[bi];
        open.RemoveAt(bi);
        return node;
    }

    private List<Vector2> Reconstruct(
        (int, int) current,
        Dictionary<(int, int), (int, int)> cameFrom,
        Dictionary<(int, int), int> cameDir,
        float startX, float startZ, float goalX, float goalZ,
        W3MovementType movement, float collisionSize,
        bool allowSmoothing, bool useCorners, float weightWalls)
    {
        _ = weightWalls;
        var total = new List<Vector2>();
        var cur = current;
        int? lastDir = null;

        if (cameFrom.ContainsKey(cur)
            && PathableBetween(NodePos(cur, useCorners).X, NodePos(cur, useCorners).Y, goalX, goalZ, movement, collisionSize)
            && PathableBetween(
                NodePos(cameFrom[cur], useCorners).X, NodePos(cameFrom[cur], useCorners).Y,
                NodePos(cur, useCorners).X, NodePos(cur, useCorners).Y, movement, collisionSize)
            && PathableBetween(
                NodePos(cameFrom[cur], useCorners).X, NodePos(cameFrom[cur], useCorners).Y,
                goalX, goalZ, movement, collisionSize)
            && allowSmoothing)
        {
            total.Insert(0, new Vector2(goalX, goalZ));
            cur = cameFrom[cur];
        }
        else
        {
            total.Insert(0, new Vector2(goalX, goalZ));
            total.Insert(0, NodePos(cur, useCorners));
        }

        lastDir = cameDir.GetValueOrDefault(cur, -1);
        if (lastDir == -1) lastDir = null;
        (int, int)? lastNode = null;
        int steps = 0;
        int limit = _grid!.Width * _grid.Height + 8;
        while (cameFrom.ContainsKey(cur))
        {
            lastNode = cur;
            cur = cameFrom[cur];
            int curDir = cameDir.GetValueOrDefault(cur, -1);
            if (lastDir == null || curDir != lastDir || curDir < 0)
            {
                Vector2 curP = NodePos(cur, useCorners);
                Vector2 lastP = lastNode.HasValue ? NodePos(lastNode.Value, useCorners) : curP;
                if (curDir >= 0
                    || lastNode == null
                    || !PathableBetween(startX, startZ, curP.X, curP.Y, movement, collisionSize)
                    || !PathableBetween(curP.X, curP.Y, lastP.X, lastP.Y, movement, collisionSize)
                    || !PathableBetween(startX, startZ, lastP.X, lastP.Y, movement, collisionSize)
                    || !allowSmoothing)
                {
                    total.Insert(0, curP);
                    lastDir = curDir >= 0 ? curDir : null;
                }
            }
            steps++;
            if (steps > limit)
                return new List<Vector2>();
        }
        return total;
    }
}
