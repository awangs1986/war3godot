using System.Numerics;

namespace W3Editor.Services;

/// <summary>Engine-independent HiveWE camera translation formulas.</summary>
public static class EditorCameraMath
{
    public const float TileSize = 128f;

    public static Vector3 KeyPanDelta(float yaw, float distanceWorld, float rightAxis, float forwardAxis, float deltaSeconds)
    {
        if (rightAxis == 0f && forwardAxis == 0f) return Vector3.Zero;
        Vector3 right = new(-MathF.Cos(yaw), 0f, MathF.Sin(yaw));
        Vector3 forward = new(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        float speed = 40f * (distanceWorld / TileSize) / 30f * deltaSeconds * TileSize;
        return right * (rightAxis * speed) + forward * (forwardAxis * speed);
    }
}
