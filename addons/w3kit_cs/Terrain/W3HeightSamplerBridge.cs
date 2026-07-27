using Godot;

namespace Diablo1Like.W3.Terrain;

/// <summary>把 W3TerrainCore.W3HeightSampler 暴露给 GDScript。</summary>
[GlobalClass]
public partial class W3HeightSamplerBridge : RefCounted
{
    private W3HeightSampler? _sampler;

    /// <summary>从 Godot 地图对象（W3EMap / W3LevelData / 打包地形）绑定采样器。</summary>
    public void BindMap(GodotObject? mapResource)
    {
        if (mapResource == null)
        {
            _sampler = null;
            return;
        }

        W3TerrainMap map = W3TerrainMapBridge.FromGodotResource(mapResource);
        _sampler = new W3HeightSampler(map);
    }

    public void bind_map(GodotObject? mapResource) => BindMap(mapResource);

    /// <summary>世界 XZ 高度采样。</summary>
    public float SampleY(float worldX, float worldZ) =>
        _sampler?.SampleY(worldX, worldZ) ?? 0f;

    public float sample_y(float worldX, float worldZ) => SampleY(worldX, worldZ);

    /// <summary>世界 → 瓦片。</summary>
    public Vector2I WorldToTile(float worldX, float worldZ)
    {
        if (_sampler == null)
            return Vector2I.Zero;
        (int x, int y) = _sampler.WorldToTile(worldX, worldZ);
        return new Vector2I(x, y);
    }

    public Vector2I world_to_tile(float worldX, float worldZ) => WorldToTile(worldX, worldZ);

    /// <summary>瓦片中心世界坐标。</summary>
    public Vector3 TileCenterWorld(Vector2I tile)
    {
        if (_sampler == null)
            return Vector3.Zero;
        (float x, float y, float z) = _sampler.TileCenterWorld(tile.X, tile.Y);
        return new Vector3(x, y, z);
    }

    public Vector3 tile_center_world(Vector2I tile) => TileCenterWorld(tile);

    public bool IsTileWalkable(Vector2I tile) =>
        _sampler?.IsTileWalkable(tile.X, tile.Y) ?? false;

    public bool is_tile_walkable(Vector2I tile) => IsTileWalkable(tile);

    public bool CanStep(Vector2I fromTile, Vector2I toTile) =>
        _sampler?.CanStep(fromTile.X, fromTile.Y, toTile.X, toTile.Y) ?? false;

    public bool can_step(Vector2I fromTile, Vector2I toTile) => CanStep(fromTile, toTile);
}
