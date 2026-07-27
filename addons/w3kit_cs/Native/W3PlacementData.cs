using Godot;

namespace Diablo1Like.MapRuntime;

/// <summary>与 W3DooParser.Placement 字段对齐的 C# 摆放数据，供批处理/模拟消费。</summary>
[GlobalClass]
public partial class W3PlacementData : RefCounted
{
    public string Id { get; set; } = "";
    public int Variation { get; set; }
    public Vector3 Position { get; set; } = Vector3.Zero;
    public float Angle { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;
    public string SkinId { get; set; } = "";
    public int Flags { get; set; }
    public int PlayerOwner { get; set; }
    public int Life { get; set; } = 100;
    public bool IsUnit { get; set; }
    public string ModelPath { get; set; } = "";
    public int CreationNumber { get; set; }

    // GDS W3DooParser.Placement 字段别名
    public string id { get => Id; set => Id = value; }
    public int variation { get => Variation; set => Variation = value; }
    public Vector3 position { get => Position; set => Position = value; }
    public float angle { get => Angle; set => Angle = value; }
    public Vector3 scale { get => Scale; set => Scale = value; }
    public string skin_id { get => SkinId; set => SkinId = value; }
    public int flags { get => Flags; set => Flags = value; }
    public int player_owner { get => PlayerOwner; set => PlayerOwner = value; }
    public int life { get => Life; set => Life = value; }
    public bool is_unit { get => IsUnit; set => IsUnit = value; }
    public string model_path { get => ModelPath; set => ModelPath = value; }
    public int creation_number { get => CreationNumber; set => CreationNumber = value; }
}
