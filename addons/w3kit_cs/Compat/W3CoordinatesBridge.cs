using Godot;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using W3Compat.Coordinates;

namespace Diablo1Like.W3.Compat;

/// <summary>将纯 C# 坐标转换适配为 Godot 向量类型（供 GDScript 调用）。</summary>
[GlobalClass]
public partial class W3CoordinatesBridge : RefCounted
{
    /// <summary>将 W3 坐标转换为 Godot 坐标。</summary>
    public Vector3 W3ToGodotAxes(Vector3 w3) =>
        ToGodot(W3Coordinates.W3ToGodotAxes(ToNumerics(w3)));

    public Vector3 w3_to_godot_axes(Vector3 w3) => W3ToGodotAxes(w3);

    /// <summary>将 Godot 坐标转换为 W3 坐标。</summary>
    public Vector3 GodotAxesToW3(Vector3 godot) =>
        ToGodot(W3Coordinates.GodotAxesToW3(ToNumerics(godot)));

    public Vector3 godot_axes_to_w3(Vector3 godot) => GodotAxesToW3(godot);

    /// <summary>将 W3 地面坐标转换为 Godot 世界坐标。</summary>
    public Vector3 GroundToGodotAxes(float w3X, float w3Y, float godotHeight = 0f) =>
        ToGodot(W3Coordinates.GroundToGodotAxes(w3X, w3Y, godotHeight));

    public Vector3 ground_to_godot_axes(float w3X, float w3Y, float godotHeight = 0f) =>
        GroundToGodotAxes(w3X, w3Y, godotHeight);

    /// <summary>将 Godot 世界坐标投影到 W3 地面坐标。</summary>
    public Vector2 GodotAxesToGround(Vector3 godot)
    {
        NumericsVector2 ground = W3Coordinates.GodotAxesToGround(ToNumerics(godot));
        return new Vector2(ground.X, ground.Y);
    }

    public Vector2 godot_axes_to_ground(Vector3 godot) => GodotAxesToGround(godot);

    /// <summary>将 W3 角度朝向转换为 Godot 偏航角。</summary>
    public float FacingDegToGodotYaw(float facingDegrees) =>
        W3Coordinates.FacingDegToGodotYaw(facingDegrees);

    public float facing_deg_to_godot_yaw(float facingDegrees) => FacingDegToGodotYaw(facingDegrees);

    /// <summary>将 W3 弧度朝向转换为 Godot 偏航角。</summary>
    public float FacingRadToGodotYaw(float facingRadians) =>
        W3Coordinates.FacingRadToGodotYaw(facingRadians);

    public float facing_rad_to_godot_yaw(float facingRadians) => FacingRadToGodotYaw(facingRadians);

    /// <summary>计算 Godot Vector2 表示的 W3 地面朝向。</summary>
    public Vector2 FacingDegToDir(float facingDegrees)
    {
        NumericsVector2 direction = W3Coordinates.FacingDegToDir(facingDegrees);
        return new Vector2(direction.X, direction.Y);
    }

    public Vector2 facing_deg_to_dir(float facingDegrees) => FacingDegToDir(facingDegrees);

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToGodot(NumericsVector3 value) => new(value.X, value.Y, value.Z);
}
