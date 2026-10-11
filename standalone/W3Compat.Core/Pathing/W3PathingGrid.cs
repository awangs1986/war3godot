using W3Compat.Formats;

namespace W3Compat.Pathing;

/// <summary>
/// Warsmash PathingGrid 静态网格（32 世界单位/格），含动态单位碰撞叠加与 foot32 膨胀缓存。
/// </summary>
public sealed class W3PathingGrid
{
    public const float CellSize = 32f;
    /// <summary>遗留别名，与 Warsmash 源码命名一致。</summary>
    public const float Cell = CellSize;

    public const int Unwalkable = 0x2;
    public const int Unflyable = 0x4;
    public const int Unbuildable = 0x8;
    public const int BlockVision = 0x10;
    public const int Blighted = 0x20;
    public const int Unswimable = 0x40;

    /// <summary>最近一次 load_bytes 的错误信息。</summary>
    public static string LastError { get; private set; } = string.Empty;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CenterOffsetX { get; set; }
    public float CenterOffsetY { get; set; }
    public byte[] Cells { get; private set; } = Array.Empty<byte>();
    public byte[] DynamicOverlay { get; private set; } = Array.Empty<byte>();

    private byte[] _foot32 = Array.Empty<byte>();

    /// <summary>从 war3map.wpm 字节加载。</summary>
    public static W3PathingGrid? LoadBytes(byte[] bytes, float centerOffsetX, float centerOffsetY)
    {
        LastError = string.Empty;
        try
        {
            W3PathingMap map = W3PathingMap.LoadBytes(bytes);
            return FromCells(map.Width, map.Height, centerOffsetX, centerOffsetY, map.Cells);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>从已解析 WPM 单元格数组构造。</summary>
    public static W3PathingGrid FromCells(int width, int height, float centerOffsetX, float centerOffsetY, byte[] cells)
    {
        var grid = new W3PathingGrid
        {
            Width = width,
            Height = height,
            CenterOffsetX = centerOffsetX,
            CenterOffsetY = centerOffsetY,
            Cells = cells,
        };
        return grid;
    }

    public int GetCellX(float x) => (int)((x - CenterOffsetX) / CellSize);
    public int GetCellY(float y) => (int)((y - CenterOffsetY) / CellSize);

    public float GetWorldX(int cellX) => cellX * CellSize + CenterOffsetX + 16f;
    public float GetWorldY(int cellY) => cellY * CellSize + CenterOffsetY + 16f;

    public float GetWorldXFromCorner(int cornerX) => cornerX * CellSize + CenterOffsetX;
    public float GetWorldYFromCorner(int cornerY) => cornerY * CellSize + CenterOffsetY;

    public int GetCornerX(float x) => (int)((x + 16f - CenterOffsetX) / CellSize);
    public int GetCornerY(float y) => (int)((y + 16f - CenterOffsetY) / CellSize);

    public bool Contains(float x, float y)
    {
        int cx = GetCellX(x);
        int cy = GetCellY(y);
        return cx >= 0 && cy >= 0 && cx < Width && cy < Height;
    }

    public int GetCellPathing(int cellX, int cellY)
    {
        int index = cellY * Width + cellX;
        if (index < 0 || index >= Cells.Length)
            return 0;
        int value = Cells[index];
        if (DynamicOverlay.Length > 0)
            value |= DynamicOverlay[index];
        return value;
    }

    /// <summary>把单位碰撞矩形烘焙进动态叠加层。</summary>
    public void BlitUnitCollision(float unitX, float unitY, float collisionSize)
    {
        if (collisionSize <= 0f)
            return;
        if (DynamicOverlay.Length == 0)
            DynamicOverlay = new byte[Width * Height];
        int minCx = Math.Clamp(GetCellX(unitX - collisionSize), 0, Width - 1);
        int maxCx = Math.Clamp(GetCellX(unitX + collisionSize - 0.01f), 0, Width - 1);
        int minCy = Math.Clamp(GetCellY(unitY - collisionSize), 0, Height - 1);
        int maxCy = Math.Clamp(GetCellY(unitY + collisionSize - 0.01f), 0, Height - 1);
        for (int cy = minCy; cy <= maxCy; cy++)
        {
            int row = cy * Width;
            for (int cx = minCx; cx <= maxCx; cx++)
                DynamicOverlay[row + cx] |= (byte)Unwalkable;
        }
        _foot32 = Array.Empty<byte>();
    }

    public int GetPathing(float x, float y) => GetCellPathing(GetCellX(x), GetCellY(y));

    public static bool FlagSet(int pathingValue, int flag) => (pathingValue & flag) != 0;

    public static bool MovementOk(int pathingValue, W3MovementType movement) => movement switch
    {
        W3MovementType.Fly => !FlagSet(pathingValue, Unflyable),
        W3MovementType.Float => !FlagSet(pathingValue, Unswimable),
        W3MovementType.Amphibious => !FlagSet(pathingValue, Unwalkable) || !FlagSet(pathingValue, Unswimable),
        W3MovementType.Disabled => true,
        _ => !FlagSet(pathingValue, Unwalkable),
    };

    public bool IsPathableMovement(float x, float y, W3MovementType movement) =>
        MovementOk(GetPathing(x, y), movement);

    public bool IsPathable(float x, float y, W3MovementType movement, float collisionSize)
    {
        if (collisionSize <= 0f)
            return Contains(x, y) && IsPathableMovement(x, y, movement);
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                float px = x + i * collisionSize;
                float py = y + j * collisionSize;
                if (!Contains(px, py) || !IsPathableMovement(px, py, movement))
                    return false;
            }
        }
        return true;
    }

    public bool IsCellPathable(int cellX, int cellY, W3MovementType movement, float collisionSize = 0f) =>
        IsPathable(GetWorldX(cellX), GetWorldY(cellY), movement, collisionSize);

    public bool IsPathableFoot(float x, float y, float collisionSize)
    {
        if (collisionSize == 0f)
            return Contains(x, y) && (GetPathing(x, y) & Unwalkable) == 0;
        if (Math.Abs(collisionSize - 32f) < 0.001f)
        {
            int cx = GetCellX(x);
            int cy = GetCellY(y);
            if (cx < 0 || cy < 0 || cx >= Width || cy >= Height)
                return false;
            EnsureFoot32();
            return _foot32[cy * Width + cx] != 0;
        }
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                float px = x + i * collisionSize;
                float py = y + j * collisionSize;
                if (!Contains(px, py) || (GetPathing(px, py) & Unwalkable) != 0)
                    return false;
            }
        }
        return true;
    }

    private void EnsureFoot32()
    {
        if (_foot32.Length > 0)
            return;
        int count = Width * Height;
        var walkable = new byte[count];
        bool hasOverlay = DynamicOverlay.Length > 0;
        for (int i = 0; i < count; i++)
        {
            int v = Cells[i];
            if (hasOverlay)
                v |= DynamicOverlay[i];
            walkable[i] = (v & Unwalkable) == 0 ? (byte)1 : (byte)0;
        }
        var horizontal = new byte[count];
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            horizontal[row] = 0;
            horizontal[row + Width - 1] = 0;
            for (int x = 1; x < Width - 1; x++)
            {
                int i = row + x;
                horizontal[i] = (byte)(walkable[i - 1] & walkable[i] & walkable[i + 1]);
            }
        }
        _foot32 = new byte[count];
        for (int x = 0; x < Width; x++)
        {
            _foot32[x] = 0;
            _foot32[(Height - 1) * Width + x] = 0;
        }
        for (int y = 1; y < Height - 1; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int i = row + x;
                _foot32[i] = (byte)(horizontal[i - Width] & horizontal[i] & horizontal[i + Width]);
            }
        }
    }
}
