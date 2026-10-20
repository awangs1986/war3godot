// W3eMap.cs —— war3map.w3e 的最小读取器
// 为什么自己写：参考实现要能直接吃官方编辑器存出来的数据，才能逐角点对照。
// 只读不写；字段名尽量贴近 w3e 格式本身，方便你对照第 3 章的格式说明。
namespace TerrainReference;

/// <summary>一个“角点”（tilepoint）。地形的所有数据都存在角点上，而不是格子里。</summary>
public sealed class Corner
{
    public int Height;        // groundHeight 原始值，8192 = 0，512 raw = 1 层
    public int WaterHeight;   // waterHeight 原始值（低 14 位）
    public bool MapEdge;      // 第 15 位：地图边界标记
    public int Texture;       // 地表贴图下标（指向文件头里的 ground 列表）
    public bool Ramp, Blight, Water, Boundary;
    public int GroundVariation, CliffVariation;
    public int CliffTexture;  // 悬崖类型下标（指向文件头里的 cliff 列表），15 = 未设置
    public int Layer;         // 悬崖层级 0..15

    public Corner Clone() => (Corner)MemberwiseClone();
}

public sealed class W3eMap
{
    public char Tileset;
    public List<string> GroundIds = new();
    public List<string> CliffIds = new();
    public int Width, Height;
    public Corner[] Corners = Array.Empty<Corner>();

    public Corner this[int x, int y] => Corners[y * Width + x];
    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public W3eMap Clone()
    {
        var m = (W3eMap)MemberwiseClone();
        m.Corners = Corners.Select(c => c.Clone()).ToArray();
        return m;
    }

    public static W3eMap Load(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        var m = new W3eMap();
        r.ReadBytes(4);                 // "W3E!"
        r.ReadInt32();                  // 版本 11
        m.Tileset = (char)r.ReadByte(); // 主 tileset，例如 'L'、'A'
        r.ReadInt32();                  // 是否自定义 tileset
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) m.GroundIds.Add(new string(r.ReadChars(4)));
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) m.CliffIds.Add(new string(r.ReadChars(4)));
        m.Width = r.ReadInt32(); m.Height = r.ReadInt32();
        r.ReadSingle(); r.ReadSingle(); // 地图原点偏移，本项目用不到
        m.Corners = new Corner[m.Width * m.Height];
        for (int i = 0; i < m.Corners.Length; i++)
        {
            short h = r.ReadInt16(); ushort wt = r.ReadUInt16();
            byte f = r.ReadByte(), v = r.ReadByte(), cl = r.ReadByte();
            m.Corners[i] = new Corner
            {
                Height = h, WaterHeight = wt & 0x3FFF, MapEdge = (wt & 0x4000) != 0,
                Texture = f & 15, Ramp = (f & 0x10) != 0, Blight = (f & 0x20) != 0,
                Water = (f & 0x40) != 0, Boundary = (f & 0x80) != 0,
                GroundVariation = v & 31, CliffVariation = v >> 5,
                CliffTexture = cl >> 4, Layer = cl & 15,
            };
        }
        return m;
    }
}
