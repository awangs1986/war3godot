using Godot;

namespace Diablo1Like.Runtime.World;

/// <summary>
/// 仅用 Geometry 静态碰撞体在运行时烘焙导航网格（不做 GPU mesh 回读）。
/// 迁移自 scripts/navigation_setup.gd。
/// </summary>
[GlobalClass]
public partial class NavigationBakeController : NavigationRegion3D
{
    [Signal]
    public delegate void NavigationReadyEventHandler();

    [Export] public NodePath GeometryRootPath = new("../Geometry");
    [Export] public float AgentRadius = 0.5f;
    [Export] public float AgentHeight = 1.5f;
    [Export] public float CellSize = 0.25f;
    [Export] public float CellHeight = 0.25f;

    public bool NavBakeDone;
    public int PolygonCount;

    public override void _Ready()
    {
        AddToGroup("nav_setup");
        CallDeferred(nameof(Bake));
    }

    private void Bake()
    {
        Node? geomRoot = GetNodeOrNull(GeometryRootPath);
        if (geomRoot == null)
        {
            GD.PushWarning($"NavigationBakeController: geometry root missing: {GeometryRootPath}");
            NavBakeDone = true;
            EmitSignal(SignalName.NavigationReady);
            return;
        }

        var navMesh = new NavigationMesh
        {
            AgentRadius = AgentRadius,
            AgentHeight = AgentHeight,
            AgentMaxClimb = 0.25f,
            AgentMaxSlope = 45.0f,
            CellSize = CellSize,
            CellHeight = CellHeight,
            // 只用碰撞体——避免运行时 GPU mesh 回读造成的卡顿/黑屏。
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometryCollisionMask = 1,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.RootNodeChildren,
        };

        var source = new NavigationMeshSourceGeometryData3D();
        NavigationServer3D.ParseSourceGeometryData(navMesh, source, geomRoot);
        NavigationServer3D.BakeFromSourceGeometryData(navMesh, source);
        NavigationMesh = navMesh;
        PolygonCount = navMesh.GetPolygonCount();
        if (PolygonCount <= 0)
            GD.PushWarning("NavigationBakeController: bake produced 0 polygons — agents will use direct move fallback.");

        FinishBakeAsync();
    }

    private async void FinishBakeAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        NavBakeDone = true;
        EmitSignal(SignalName.NavigationReady);
    }
}
