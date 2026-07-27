using System.Buffers.Binary;
using System.Text;

namespace W3Compat.Formats;

/// <summary>war3map.wpm 静态寻路网格，每个格子对应 32 个世界单位。</summary>
public sealed class W3PathingMap
{
    public const float CellSize = 32f;
    public const int Unwalkable = 0x2;
    public const int Unflyable = 0x4;
    public const int Unbuildable = 0x8;
    public const int Blight = 0x20;
    public const int Water = 0x40;

    /// <summary>文件头中的版本字段（offset 4）。</summary>
    public int Version;

    /// <summary>网格宽度。</summary>
    public int Width;

    /// <summary>网格高度。</summary>
    public int Height;

    /// <summary>按行优先顺序存储的寻路标志字节。</summary>
    public byte[] Cells = Array.Empty<byte>();

    /// <summary>从磁盘加载 war3map.wpm 文件。</summary>
    public static W3PathingMap Load(string path) => LoadBytes(File.ReadAllBytes(path));

    /// <summary>尝试解析；失败返回 null（对齐 GDS 语义）。</summary>
    public static W3PathingMap? TryLoadBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 12)
            return null;
        try
        {
            return LoadBytes(bytes);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>从完整的 war3map.wpm 字节序列解析寻路网格。</summary>
    public static W3PathingMap LoadBytes(byte[] bytes)
    {
        if (bytes.Length < 16)
            throw new InvalidDataException("wpm 文件过短");
        if (bytes[0] != 'M' || bytes[1] != 'P' || bytes[2] != '3' || bytes[3] != 'W')
            throw new InvalidDataException("wpm 魔数错误");

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (width <= 0 || height <= 0 || bytes.Length < 16 + (long)width * height)
            throw new InvalidDataException($"wpm 数据被截断: {width}x{height}");

        var grid = new W3PathingMap
        {
            Version = version,
            Width = width,
            Height = height,
            Cells = new byte[checked(width * height)],
        };
        Array.Copy(bytes, 16, grid.Cells, 0, grid.Cells.Length);
        return grid;
    }

    /// <summary>按 HiveWE 的 MP3W 布局序列化寻路网格（保留 Version）。</summary>
    public byte[] SaveBytes()
    {
        if (Width <= 0 || Height <= 0 || Cells.Length != checked(Width * Height))
            throw new InvalidDataException(
                $"invalid pathing grid {Width}x{Height} with {Cells.Length} cells");

        byte[] bytes = new byte[16 + Cells.Length];
        Encoding.ASCII.GetBytes("MP3W", bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), Height);
        Cells.CopyTo(bytes, 16);
        return bytes;
    }

    /// <summary>读取指定网格坐标的寻路标志，边界外返回零。</summary>
    public int GetCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return 0;
        return Cells[y * Width + x];
    }
}
