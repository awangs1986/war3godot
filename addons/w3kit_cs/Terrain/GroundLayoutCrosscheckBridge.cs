using Godot;
using System.Linq;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// 测试用桥：供 tools/W3GroundCrosscheck.cs 对照 HiveWE 角点贴图期望。
/// 一次性构建生产侧 WarsmashTerrainRules + GroundLayout，暴露角点/瓦片查询。
/// </summary>
[GlobalClass]
public partial class GroundLayoutCrosscheckBridge : RefCounted
{
    private W3TerrainMap? _map;
    private WarsmashTerrainRules? _rules;
    private GroundLayout? _layout;

    public void Prepare(GodotObject mapResource, int[] cliffToGround, byte[] tilesetExtended, bool allowRampModels)
    {
        _map = W3TerrainMapBridge.FromGodotResource(mapResource);
        _rules = new WarsmashTerrainRules(_map, modelExists: _ => allowRampModels);
        _rules.RebuildCliffs();
        bool[] extendedFlags = tilesetExtended.Select(value => value != 0).ToArray();
        _layout = new GroundLayout(_map, _rules, extendedFlags, cliffToGround, _map.GroundTilesets.Count + 1);
    }

    public void prepare(GodotObject mapResource, int[] cliffToGround, byte[] tilesetExtended, bool allowRampModels) =>
        Prepare(mapResource, cliffToGround, tilesetExtended, allowRampModels);

    /// <summary>HiveWE Terrain::real_tile_texture (seal authority) for one corner.</summary>
    public int CornerTexture(int x, int y) => _layout!.RealTileTexture(x, y);

    public int corner_texture(int x, int y) => CornerTexture(x, y);

    public bool TileExists(int x, int y) => _rules!.GroundExists(x, y);

    public bool tile_exists(int x, int y) => TileExists(x, y);

    public float[] BuildTileData() => _layout!.BuildTileData();

    public float[] build_tile_data() => BuildTileData();
}
