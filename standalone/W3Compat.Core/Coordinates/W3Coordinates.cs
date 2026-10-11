using System.Numerics;

namespace W3Compat.Coordinates;

/// <summary>Warcraft III 与 Godot 坐标约定之间的纯数学转换。</summary>
public static class W3Coordinates
{
    /// <summary>将 W3 的 X、Y、Z 坐标转换为 Godot 的 X、Y、Z 轴排列。</summary>
    public static Vector3 W3ToGodotAxes(Vector3 w3) => new(w3.X, w3.Z, w3.Y);

    /// <summary>将 Godot 的 X、Y、Z 坐标转换为 W3 的 X、Y、Z 轴排列。</summary>
    public static Vector3 GodotAxesToW3(Vector3 godot) => new(godot.X, godot.Z, godot.Y);

    /// <summary>将 W3 地面平面坐标转换为给定高度的 Godot 世界坐标。</summary>
    public static Vector3 GroundToGodotAxes(float w3X, float w3Y, float godotHeight = 0f) =>
        new(w3X, godotHeight, w3Y);

    /// <summary>将 Godot 世界坐标投影为 W3 地面平面坐标。</summary>
    public static Vector2 GodotAxesToGround(Vector3 godot) => new(godot.X, godot.Z);

    /// <summary>将 W3 朝向角度转换为 Godot 的 Y 轴弧度偏航角。</summary>
    public static float FacingDegToGodotYaw(float facingDegrees) =>
        -facingDegrees * (MathF.PI / 180f);

    /// <summary>将 W3 朝向弧度转换为 Godot 的 Y 轴弧度偏航角。</summary>
    public static float FacingRadToGodotYaw(float facingRadians) => -facingRadians;

    /// <summary>计算 W3 地面平面上指定朝向角度的单位方向向量。</summary>
    public static Vector2 FacingDegToDir(float facingDegrees)
    {
        float radians = facingDegrees * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }
}
