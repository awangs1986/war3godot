using System.Numerics;

namespace W3Editor.Rendering;

/// <summary>
/// HiveWE-style editor camera (faithful port of HiveWE src/base/camera.ixx),
/// converted from HiveWE's Z-up tile units to our Y-up world units
/// (1 HiveWE unit = 1 tile = 128 world units).
///
/// Feel-critical constants copied verbatim from HiveWE:
///  - right-drag pans the look-at point on the ground: 0.025 * (distance/30) tiles per pixel;
///  - Ctrl+right-drag rotates: 0.0025 rad per pixel, pitch clamped just short of ±90°;
///  - wheel zooms: distance *= 0.999^angleDelta (one notch = 120), clamped;
///  - arrow keys pan at 40 * (distance/30) tiles per second.
///
/// Mapping: HiveWE horizontal_angle h → Yaw = h + π; vertical_angle v → Pitch = -v.
/// </summary>
public sealed class EditorCamera
{
    private const float Tile = 128f;

    /// <summary>Look-at point on the ground (HiveWE "position").</summary>
    public Vector3 Target;
    /// <summary>Eye-to-target distance in world units. HiveWE default = 20 tiles.</summary>
    public float Distance = 20f * Tile;
    /// <summary>Yaw in radians. Default π corresponds to HiveWE horizontal_angle = 0 (looking north).</summary>
    public float Yaw = MathF.PI;
    /// <summary>Pitch above the horizon (radians). HiveWE default vertical_angle = -0.977.</summary>
    public float Pitch = 0.977f;

    public float FieldOfView = 50f * MathF.PI / 180f;   // HiveWE fov = 50
    public float NearPlane = 0.05f * Tile;               // HiveWE draw_distance_close = 0.05
    public float FarPlane = 2000f * Tile;                // HiveWE draw_distance_far = 2000

    public Vector3 Position
    {
        get
        {
            // HiveWE eye = position - direction * distance; "back" = -direction mapped to Y-up.
            float cosPitch = MathF.Cos(Pitch);
            var back = new Vector3(MathF.Sin(Yaw) * cosPitch, MathF.Sin(Pitch), MathF.Cos(Yaw) * cosPitch);
            return Target + back * Distance;
        }
    }

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    public Matrix4x4 Projection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, MathF.Max(aspect, 0.05f), NearPlane, FarPlane);

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    /// <summary>HiveWE X axis (screen-right projected on the ground).</summary>
    private Vector3 GroundRight => new(-MathF.Cos(Yaw), 0f, MathF.Sin(Yaw));

    /// <summary>HiveWE "forward" axis (points toward the camera on the ground, i.e. screen-down).</summary>
    private Vector3 GroundForward => new(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));

    /// <summary>Ctrl+right-drag. HiveWE: h += dx*0.0025; v -= dy*0.0025 (v = -Pitch).</summary>
    public void Rotate(float dxPixels, float dyPixels)
    {
        Yaw += dxPixels * 0.0025f;
        Pitch += dyPixels * 0.0025f;
        Pitch = Math.Clamp(Pitch, -MathF.PI / 2f + 0.001f, MathF.PI / 2f - 0.001f);
    }

    /// <summary>Right-drag pan. HiveWE: position += X*(-dx*k) + forward*(-dy*k), k = 0.025*(distance/30).</summary>
    public void Pan(float dxPixels, float dyPixels)
    {
        float k = 0.025f * (Distance / Tile) / 30f * Tile;   // world units per pixel
        Target += GroundRight * (-dxPixels * k) + GroundForward * (-dyPixels * k);
    }

    /// <summary>Wheel zoom. HiveWE: distance *= 0.999^angleDelta (one Qt notch = 120).</summary>
    public void Zoom(float wheelNotches)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(0.999f, wheelNotches * 120f), 0.25f * Tile, 1000f * Tile);
    }

    /// <summary>
    /// Arrow-key pan, called once per rendered frame. HiveWE: 40 * (distance/30) tiles/s.
    /// rightAxis: Left = -1 / Right = +1; forwardAxis: Up = -1 / Down = +1.
    /// </summary>
    public void KeyPan(float rightAxis, float forwardAxis, float deltaSeconds)
    {
        if (rightAxis == 0f && forwardAxis == 0f)
            return;
        float speed = 40f * (Distance / Tile) / 30f * deltaSeconds * Tile;
        Target += GroundRight * (rightAxis * speed) + GroundForward * (forwardAxis * speed);
    }

    /// <summary>HiveWE Camera::reset — restores angles and distance, keeps the target.</summary>
    public void Reset()
    {
        Distance = 20f * Tile;
        Yaw = MathF.PI;
        Pitch = 0.977f;
    }

    /// <summary>Builds the world-space picking ray for a viewport pixel.</summary>
    public (Vector3 Origin, Vector3 Direction) PixelRay(float px, float py, float width, float height)
    {
        float ndcX = px / MathF.Max(width, 1f) * 2f - 1f;
        float ndcY = 1f - py / MathF.Max(height, 1f) * 2f;
        Matrix4x4.Invert(ViewProjection(width / MathF.Max(height, 1f)), out Matrix4x4 inverse);
        Vector4 near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), inverse);
        Vector4 far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
        Vector3 origin = new Vector3(near.X, near.Y, near.Z) / near.W;
        Vector3 target = new Vector3(far.X, far.Y, far.Z) / far.W;
        return (origin, Vector3.Normalize(target - origin));
    }

    /// <summary>Centers the camera on a map region (used when a map is opened).</summary>
    public void Frame(float centerX, float centerZ, float extent)
    {
        Target = new Vector3(centerX, 0f, centerZ);
        Distance = Math.Clamp(extent * 1.1f, 600f, 1000f * Tile);
    }
}
