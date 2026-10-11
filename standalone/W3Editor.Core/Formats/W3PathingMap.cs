namespace W3Editor.Formats;

/// <summary>
/// war3map.wpm static pathing grid — port of addons/w3kit/w3_pathing_grid.gd
/// (Warsmash PathingGrid). 32 world units per cell, W3 (x,y) plane.
/// </summary>
public sealed class W3PathingMap
{
    public const float CellSize = 32f;
    public const int Unwalkable = 0x2;
    public const int Unflyable = 0x4;
    public const int Unbuildable = 0x8;

    public int Width;
    public int Height;
    public byte[] Cells = Array.Empty<byte>();

    public static W3PathingMap Load(string path) => LoadBytes(File.ReadAllBytes(path));

    public static W3PathingMap LoadBytes(byte[] bytes)
    {
        if (bytes.Length < 16)
            throw new InvalidDataException("wpm 文件过短");
        if (bytes[0] != 'M' || bytes[1] != 'P' || bytes[2] != '3' || bytes[3] != 'W')
            throw new InvalidDataException("wpm 魔数错误");
        int w = BitConverter.ToInt32(bytes, 8);
        int h = BitConverter.ToInt32(bytes, 12);
        if (w <= 0 || h <= 0 || bytes.Length < 16 + (long)w * h)
            throw new InvalidDataException($"wpm 数据被截断: {w}x{h}");
        var grid = new W3PathingMap { Width = w, Height = h, Cells = new byte[w * h] };
        Array.Copy(bytes, 16, grid.Cells, 0, w * h);
        return grid;
    }

    public int GetCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        return Cells[y * Width + x];
    }
}
