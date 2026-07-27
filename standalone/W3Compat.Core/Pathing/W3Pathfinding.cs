using System.Numerics;

namespace W3Compat.Pathing;

/// <summary>
/// Warsmash CPathfindingProcessor 分帧预算版：start_job/update 与 Java 原版一致。
/// </summary>
public sealed class W3Pathfinding
{
    public const int MaxIterations = 20000;
    public const int WorkBudget = 1500;

    private static readonly int[] DirX = [-1, 0, 1, 1, 1, 0, -1, -1];
    private static readonly int[] DirY = [1, 1, 1, 0, -1, -1, -1, 0];

    private readonly W3PathingGrid _grid;
    private readonly float[] _dirLen = new float[8];

    private double[] _g = Array.Empty<double>();
    private int[] _cameFrom = Array.Empty<int>();
    private int[] _cameDir = Array.Empty<int>();
    private int[] _stamp = Array.Empty<int>();
    private int _jobId;
    private bool _useCorners;
    private int _graphW;
    private int _graphH;

    private bool _active;
    private Vector2 _start;
    private Vector2 _goal;
    private float _collisionSize;
    private bool _allowSmoothing;
    private PathHeap? _open;
    private int _iterations;
    private readonly List<int> _goalIndices = new();
    private readonly List<Vector2> _goalPoints = new();
    private float _weightForHittingWalls = 1e9f;

    public W3Pathfinding(W3PathingGrid grid)
    {
        _grid = grid;
        for (int d = 0; d < 8; d++)
            _dirLen[d] = MathF.Sqrt(DirX[d] * DirX[d] + DirY[d] * DirY[d]);
    }

    public static bool IsCollisionSizeBetterSuitedForCorners(float collisionSize) =>
        ((2 * (int)collisionSize) / 32) % 2 == 1;

    public bool IsActive => _active;

    public void Cancel()
    {
        _active = false;
        _open = null;
    }

    public void StartJob(Vector2 start, Vector2 goal, float collisionSize, bool allowSmoothing)
    {
        _active = true;
        _start = start;
        _goal = goal;
        _collisionSize = collisionSize;
        _allowSmoothing = allowSmoothing;
        _iterations = 0;
        _open = new PathHeap();
        if (Vector2.Distance(start, goal) < 1e-6f)
        {
            _open = null;
            return;
        }

        _jobId++;
        _useCorners = IsCollisionSizeBetterSuitedForCorners(collisionSize);
        _graphW = _grid.Width + (_useCorners ? 1 : 0);
        _graphH = _grid.Height + (_useCorners ? 1 : 0);
        int nodeCount = _graphW * _graphH;
        if (_g.Length != nodeCount)
        {
            _g = new double[nodeCount];
            _cameFrom = new int[nodeCount];
            _cameDir = new int[nodeCount];
            _stamp = new int[nodeCount];
        }

        _weightForHittingWalls = 1e9f;
        if (!_grid.IsPathableFoot(goal.X, goal.Y, collisionSize))
            _weightForHittingWalls = 5e2f;

        int goalCx = MapX(goal.X);
        int goalCy = MapY(goal.Y);
        _goalIndices.Clear();
        _goalPoints.Clear();
        float bestGoalDistance = Vector2.Distance(NodePoint(goalCx, goalCy), goal);
        for (int i = goalCx - 1; i <= goalCx + 1; i++)
        {
            for (int j = goalCy - 1; j <= goalCy + 1; j++)
            {
                if (j < 0 || j >= _graphH || i < 0 || i >= _graphW)
                    continue;
                Vector2 p = NodePoint(i, j);
                if (Vector2.Distance(p, goal) <= bestGoalDistance)
                {
                    _goalIndices.Add(j * _graphW + i);
                    _goalPoints.Add(p);
                }
            }
        }

        int startCx = MapX(start.X);
        int startCy = MapY(start.Y);
        Vector2 startNodePoint = NodePoint(startCx, startCy);
        int minX = startCx;
        int maxX = startCx;
        if (start.X > startNodePoint.X) maxX = startCx + 1;
        else if (start.X < startNodePoint.X) minX = startCx - 1;
        int minY = startCy;
        int maxY = startCy;
        if (start.Y > startNodePoint.Y) maxY = startCy + 1;
        else if (start.Y < startNodePoint.Y) minY = startCy - 1;

        for (int cellX = minX; cellX <= maxX; cellX++)
        {
            for (int cellY = minY; cellY <= maxY; cellY++)
            {
                if (cellX < 0 || cellX >= _grid.Width || cellY < 0 || cellY >= _grid.Height)
                    continue;
                int index = cellY * _graphW + cellX;
                Touch(index);
                Vector2 p = NodePoint(cellX, cellY);
                double tentative = PathableBetween(start.X, start.Y, p.X, p.Y, collisionSize)
                    ? Vector2.Distance(p, start)
                    : _weightForHittingWalls;
                _g[index] = tentative;
                _open!.Push(index, tentative + H(p));
            }
        }
    }

    /// <summary>进行中返回 null，完成返回路点（可为空）。</summary>
    public List<Vector2>? Update()
    {
        if (!_active)
            return null;
        if (_open == null)
            return Finish(new List<Vector2>());
        int work = 0;
        while (!_open.IsEmpty)
        {
            int current = _open.Pop();
            Touch(current);
            if (IsGoal(current))
                return Finish(Backtrace(current));
            Vector2 currentPoint = NodePoint(current % _graphW, current / _graphW);
            for (int d = 0; d < 8; d++)
            {
                float x = currentPoint.X + DirX[d] * 32f;
                float y = currentPoint.Y + DirY[d] * 32f;
                if (!_grid.Contains(x, y))
                    continue;
                double turnCost = _cameDir[current] >= 0 && d != _cameDir[current] ? 0.25 : 0.0;
                double tentative = _g[current] + (_dirLen[d] + turnCost) * 32.0;
                if (!PathableBetween(currentPoint.X, currentPoint.Y, x, y, _collisionSize))
                    tentative += _dirLen[d] * _weightForHittingWalls;
                int neighbor = MapY(y) * _graphW + MapX(x);
                Touch(neighbor);
                if (tentative < _g[neighbor])
                {
                    _cameFrom[neighbor] = current;
                    _cameDir[neighbor] = d;
                    _g[neighbor] = tentative;
                    _open.Push(neighbor, tentative + H(NodePoint(neighbor % _graphW, neighbor / _graphW)));
                }
            }
            work++;
            _iterations++;
            if (_iterations > MaxIterations)
                return Finish(new List<Vector2>());
            if (work >= WorkBudget)
                return null;
        }
        return Finish(new List<Vector2>());
    }

    public List<Vector2> FindNaiveSlowPath(Vector2 start, Vector2 goal, float collisionSize, bool allowSmoothing)
    {
        StartJob(start, goal, collisionSize, allowSmoothing);
        while (true)
        {
            List<Vector2>? result = Update();
            if (result != null)
                return result;
        }
    }

    private List<Vector2> Finish(List<Vector2> result)
    {
        _active = false;
        _open = null;
        return result;
    }

    private int MapX(float x) => _useCorners ? _grid.GetCornerX(x) : _grid.GetCellX(x);
    private int MapY(float y) => _useCorners ? _grid.GetCornerY(y) : _grid.GetCellY(y);

    private Vector2 NodePoint(int cellX, int cellY)
    {
        if (_useCorners)
            return new Vector2(_grid.GetWorldXFromCorner(cellX), _grid.GetWorldYFromCorner(cellY));
        return new Vector2(_grid.GetWorldX(cellX), _grid.GetWorldY(cellY));
    }

    private void Touch(int index)
    {
        if (_stamp[index] != _jobId)
        {
            _g[index] = double.PositiveInfinity;
            _cameFrom[index] = -1;
            _cameDir[index] = -1;
            _stamp[index] = _jobId;
        }
    }

    private bool IsGoal(int index) => _goalIndices.Contains(index);

    private float H(Vector2 p)
    {
        float best = 0f;
        foreach (Vector2 gp in _goalPoints)
            best = MathF.Max(best, Vector2.Distance(p, gp));
        return best;
    }

    private bool PathableBetween(float startX, float startY, float x, float y, float collisionSize) =>
        _grid.IsPathableFoot(x, y, collisionSize)
        && _grid.IsPathableFoot(startX, y, collisionSize)
        && _grid.IsPathableFoot(x, startY, collisionSize);

    private List<Vector2> Backtrace(int goalIndex)
    {
        var totalPath = new List<Vector2>();
        int current = goalIndex;
        Vector2 currentPoint = NodePoint(current % _graphW, current / _graphW);
        int came = _cameFrom[current];
        if (came >= 0
            && _allowSmoothing
            && PathableBetween(currentPoint.X, currentPoint.Y, _goal.X, _goal.Y, _collisionSize)
            && PathableBetween(NodeX(came), NodeY(came), currentPoint.X, currentPoint.Y, _collisionSize)
            && PathableBetween(NodeX(came), NodeY(came), _goal.X, _goal.Y, _collisionSize))
        {
            totalPath.Add(_goal);
            current = came;
        }
        else
        {
            totalPath.Add(_goal);
            totalPath.Insert(0, currentPoint);
        }
        int lastCameFromDirection = _cameDir[current];
        int lastNode = -1;
        int stepsBackward = 0;
        while (_cameFrom[current] >= 0)
        {
            lastNode = current;
            current = _cameFrom[current];
            int currentDir = _cameDir[current];
            if (lastCameFromDirection < 0 || currentDir != lastCameFromDirection || currentDir < 0)
            {
                if (currentDir >= 0
                    || lastNode < 0
                    || !PathableBetween(_start.X, _start.Y, NodeX(current), NodeY(current), _collisionSize)
                    || !PathableBetween(NodeX(current), NodeY(current), NodeX(lastNode), NodeY(lastNode), _collisionSize)
                    || !PathableBetween(_start.X, _start.Y, NodeX(lastNode), NodeY(lastNode), _collisionSize)
                    || !_allowSmoothing)
                {
                    totalPath.Insert(0, new Vector2(NodeX(current), NodeY(current)));
                    lastCameFromDirection = currentDir;
                }
            }
            stepsBackward++;
            if (stepsBackward > _grid.Width * _grid.Height)
            {
                totalPath.Clear();
                break;
            }
        }
        return totalPath;
    }

    private float NodeX(int index) => NodePoint(index % _graphW, index / _graphW).X;
    private float NodeY(int index) => NodePoint(index % _graphW, index / _graphW).Y;

    private sealed class PathHeap
    {
        private readonly List<int> _indices = new();
        private readonly List<double> _keys = new();

        public bool IsEmpty => _indices.Count == 0;

        public void Push(int index, double key)
        {
            _indices.Add(index);
            _keys.Add(key);
            int i = _indices.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_keys[parent] <= _keys[i])
                    break;
                Swap(i, parent);
                i = parent;
            }
        }

        public int Pop()
        {
            int result = _indices[0];
            int last = _indices.Count - 1;
            _indices[0] = _indices[last];
            _keys[0] = _keys[last];
            _indices.RemoveAt(last);
            _keys.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                int right = left + 1;
                int smallest = i;
                if (left < _indices.Count && _keys[left] < _keys[smallest])
                    smallest = left;
                if (right < _indices.Count && _keys[right] < _keys[smallest])
                    smallest = right;
                if (smallest == i)
                    break;
                Swap(i, smallest);
                i = smallest;
            }
            return result;
        }

        private void Swap(int a, int b)
        {
            (_indices[a], _indices[b]) = (_indices[b], _indices[a]);
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        }
    }
}
