using Godot;

namespace Diablo1Like.MapRuntime;

/// <summary>角点数据，字段名与 GDS W3Corner 对齐供 duck-typing。</summary>
[GlobalClass]
public partial class W3CornerData : RefCounted
{
    public float GroundHeight { get; set; }
    public float WaterHeight { get; set; }
    public bool MapEdge { get; set; }
    public bool Ramp { get; set; }
    public bool Blight { get; set; }
    public bool Water { get; set; }
    public bool Boundary { get; set; }
    public int GroundTexture { get; set; }
    public int GroundVariation { get; set; }
    public int CliffVariation { get; set; }
    public int CliffTexture { get; set; }
    public int LayerHeight { get; set; }

    // GDS W3Corner 字段别名
    public float ground_height { get => GroundHeight; set => GroundHeight = value; }
    public float water_height { get => WaterHeight; set => WaterHeight = value; }
    public bool map_edge { get => MapEdge; set => MapEdge = value; }
    public bool ramp { get => Ramp; set => Ramp = value; }
    public bool blight { get => Blight; set => Blight = value; }
    public bool water { get => Water; set => Water = value; }
    public bool boundary { get => Boundary; set => Boundary = value; }
    public int ground_texture { get => GroundTexture; set => GroundTexture = value; }
    public int ground_variation { get => GroundVariation; set => GroundVariation = value; }
    public int cliff_variation { get => CliffVariation; set => CliffVariation = value; }
    public int cliff_texture { get => CliffTexture; set => CliffTexture = value; }
    public int layer_height { get => LayerHeight; set => LayerHeight = value; }

    /// <summary>最终地面高度（层高修正后）乘以瓦片尺寸。</summary>
    public float WorldY(float tileSize = 128f) =>
        (GroundHeight + LayerHeight - 2f) * tileSize;

    public float world_y(float tileSize = 128f) => WorldY(tileSize);
}
