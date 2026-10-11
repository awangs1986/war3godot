using System;
using System.Collections.Generic;
using Godot;

namespace W3Foundation.Preview;

/// <summary>
/// Camera-proximity budget for foundation map previews. Defaults preserve the
/// proven old-preview behavior: animation hysteresis and a nearest-target cap.
/// </summary>
[GlobalClass]
public partial class PreviewVisibilityBudget : Node
{
    [Export] public NodePath CameraPath { get; set; } = new();
    [Export] public float AnimationNearDistance { get; set; } = 2200.0f;
    [Export] public float AnimationFarDistance { get; set; } = 2800.0f;
    [Export] public float AnimatedDrawDistance { get; set; } = 4200.0f;
    [Export] public float StaticDrawDistance { get; set; } = 5200.0f;
    [Export] public int MaxNearAnimations { get; set; } = 48;
    [Export] public double ProximityCheckSeconds { get; set; } = 0.2;
    [Export] public double StaticCheckSeconds { get; set; } = 0.15;

    public int VisibleAnimatedCount { get; private set; }
    public int ActiveAnimationCount { get; private set; }
    public int VisibleStaticCount { get; private set; }

    private Camera3D? _camera;
    private double _animatedElapsed;
    private double _staticElapsed;

    public override void _Ready()
    {
        ResolveCamera();
    }

    public override void _Process(double delta)
    {
        ResolveCamera();
        if (_camera == null)
            return;

        _animatedElapsed += delta;
        _staticElapsed += delta;
        if (_animatedElapsed >= ProximityCheckSeconds)
        {
            _animatedElapsed = 0.0;
            EvaluateAnimated(_camera);
        }
        if (_staticElapsed >= StaticCheckSeconds)
        {
            _staticElapsed = 0.0;
            EvaluateStatic(_camera);
        }
    }

    public void EvaluateNow(Camera3D camera)
    {
        _camera = camera;
        EvaluateAnimated(camera);
        EvaluateStatic(camera);
    }

    private void ResolveCamera()
    {
        if (_camera != null && IsInstanceValid(_camera))
            return;
        if (!CameraPath.IsEmpty)
            _camera = GetNodeOrNull<Camera3D>(CameraPath);
        _camera ??= GetViewport()?.GetCamera3D();
    }

    private List<(PreviewLodTarget Target, float DistanceSquared)> CollectTargets(Camera3D camera, bool animated)
    {
        var targets = new List<(PreviewLodTarget, float)>();
        foreach (Node node in GetTree().GetNodesInGroup(PreviewLodTarget.PreviewGroup))
        {
            if (node is not PreviewLodTarget target || target.SupportsAnimation != animated)
                continue;
            targets.Add((target, camera.GlobalPosition.DistanceSquaredTo(target.GlobalPosition)));
        }
        targets.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return targets;
    }

    private void EvaluateAnimated(Camera3D camera)
    {
        List<(PreviewLodTarget Target, float DistanceSquared)> targets = CollectTargets(camera, true);
        float drawSquared = AnimatedDrawDistance * AnimatedDrawDistance;
        float nearSquared = AnimationNearDistance * AnimationNearDistance;
        float farSquared = AnimationFarDistance * AnimationFarDistance;
        int animationSlots = 0;
        VisibleAnimatedCount = 0;
        ActiveAnimationCount = 0;

        foreach ((PreviewLodTarget target, float distanceSquared) in targets)
        {
            bool visible = distanceSquared <= drawSquared;
            target.SetPreviewVisible(visible);
            if (visible)
                VisibleAnimatedCount++;

            bool inHysteresisRange = target.AnimationActive
                ? distanceSquared <= farSquared
                : distanceSquared <= nearSquared;
            bool animate = visible && inHysteresisRange && animationSlots < Math.Max(0, MaxNearAnimations);
            target.SetAnimationActive(animate);
            if (animate)
            {
                animationSlots++;
                ActiveAnimationCount++;
            }
        }
    }

    private void EvaluateStatic(Camera3D camera)
    {
        float drawSquared = StaticDrawDistance * StaticDrawDistance;
        VisibleStaticCount = 0;
        foreach ((PreviewLodTarget target, float distanceSquared) in CollectTargets(camera, false))
        {
            bool visible = distanceSquared <= drawSquared && camera.IsPositionInFrustum(target.GlobalPosition);
            target.SetPreviewVisible(visible);
            if (visible)
                VisibleStaticCount++;
        }
    }
}
