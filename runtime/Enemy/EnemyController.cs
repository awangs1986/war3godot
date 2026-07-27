using Godot;
using Diablo1Like.Runtime.Player;

namespace Diablo1Like.Runtime.Enemy;

/// <summary>
/// 出仇恨范围追击 + 导航网格寻路 + 直线兜底。迁移自 scripts/enemy.gd。
/// 避让回调不可靠——改用简单的物理分离力。
/// </summary>
[GlobalClass]
public partial class EnemyController : CharacterBody3D
{
    [Signal]
    public delegate void DiedEventHandler(Node3D enemy);

    [Export] public int MaxHp = 30;
    [Export] public int Damage = 6;
    [Export] public float AggroRange = 11.0f;
    [Export] public float LeashRange = 22.0f;
    [Export] public float AttackRange = 1.8f;
    [Export] public float MoveSpeed = 4.6f;
    [Export] public float AttackCooldown = 0.9f;
    [Export] public float PathRefresh = 0.25f;

    public int Hp;
    private float _cd;
    private bool _alive = true;
    private bool _navReady;
    private float _refreshLeft;
    private bool _aggro;

    private MeshInstance3D _mesh = null!;
    private NavigationAgent3D _agent = null!;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");

        AddToGroup("enemy");
        CollisionLayer = 4;
        CollisionMask = 1 | 4; // world + other enemies
        Hp = MaxHp;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.18f, 0.15f) };
        _mesh.MaterialOverride = mat;

        _agent.PathDesiredDistance = 0.5f;
        _agent.TargetDesiredDistance = 0.7f;
        _agent.Radius = 0.5f;
        _agent.Height = 1.4f;
        // RVO 避让在回调不触发时会让敌人卡死不动。
        _agent.AvoidanceEnabled = false;
        _agent.MaxSpeed = MoveSpeed;
        CallDeferred(nameof(WaitForNav));
    }

    private async void WaitForNav()
    {
        if (GetTree().GetFirstNodeInGroup("nav_setup") is World.NavigationBakeController navController)
        {
            if (!navController.NavBakeDone)
                await ToSignal(navController, World.NavigationBakeController.SignalName.NavigationReady);
            _agent.SetNavigationMap(navController.GetNavigationMap());
        }
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _navReady = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_alive)
            return;
        float dt = (float)delta;
        _cd = Mathf.Max(0.0f, _cd - dt);
        _refreshLeft -= dt;

        if (!IsOnFloor())
            Velocity = new Vector3(Velocity.X, Velocity.Y - 20.0f * dt, Velocity.Z);
        else
            Velocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);

        PlayerController? player = FindPlayer();
        if (player == null || !player.IsAlive())
        {
            StopHorizontal();
            MoveAndSlide();
            return;
        }

        Vector3 to = player.GlobalPosition - GlobalPosition;
        to.Y = 0.0f;
        float dist = to.Length();

        if (_aggro)
        {
            if (dist > LeashRange)
                _aggro = false;
        }
        else if (dist <= AggroRange)
        {
            _aggro = true;
        }

        if (!_aggro)
        {
            StopHorizontal();
            MoveAndSlide();
            return;
        }

        if (dist <= AttackRange)
        {
            if (_navReady)
                _agent.TargetPosition = GlobalPosition;
            StopHorizontal();
            if (to.Length() > 0.01f)
                FaceDir(to);
            if (_cd <= 0.0f)
            {
                _cd = AttackCooldown;
                player.TakeDamage(Damage);
            }
            MoveAndSlide();
            return;
        }

        if (_navReady && _refreshLeft <= 0.0f)
        {
            _refreshLeft = PathRefresh;
            _agent.TargetPosition = player.GlobalPosition;
        }

        Vector3 dir = to;
        if (_navReady && !_agent.IsNavigationFinished())
        {
            Vector3 next = _agent.GetNextPathPosition();
            Vector3 navDir = GlobalPosition.DirectionTo(next);
            navDir.Y = 0.0f;
            if (navDir.Length() > 0.05f)
                dir = navDir;
        }

        if (dir.Length() < 0.001f)
        {
            StopHorizontal();
            MoveAndSlide();
            return;
        }

        dir = dir.Normalized();
        dir = Separate(dir).Normalized();
        Velocity = new Vector3(dir.X * MoveSpeed, Velocity.Y, dir.Z * MoveSpeed);
        FaceDir(dir);
        MoveAndSlide();
    }

    private Vector3 Separate(Vector3 desired)
    {
        Vector3 push = Vector3.Zero;
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
        {
            if (n == this || n is not Node3D other)
                continue;
            Vector3 d = GlobalPosition - other.GlobalPosition;
            d.Y = 0.0f;
            float dist = d.Length();
            if (dist > 0.01f && dist < 1.4f)
                push += d.Normalized() * (1.4f - dist);
        }
        if (push.Length() < 0.001f)
            return desired;
        return desired + push * 1.5f;
    }

    private void FaceDir(Vector3 dir)
    {
        var flat = new Vector3(dir.X, 0.0f, dir.Z);
        if (flat.Length() < 0.001f)
            return;
        flat = flat.Normalized();
        Rotation = new Vector3(Rotation.X, Mathf.Atan2(flat.X, flat.Z), Rotation.Z);
    }

    private void StopHorizontal()
    {
        Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
    }

    public void TakeDamage(int amount)
    {
        if (!_alive)
            return;
        _aggro = true; // 挨打总会拉仇恨
        Hp -= amount;
        Tween tw = CreateTween();
        tw.TweenProperty(_mesh, "scale", new Vector3(1.2f, 0.8f, 1.2f), 0.05);
        tw.TweenProperty(_mesh, "scale", Vector3.One, 0.08);
        if (Hp <= 0)
            Die();
    }

    private void Die()
    {
        _alive = false;
        EmitSignal(SignalName.Died, this);
        QueueFree();
    }

    private PlayerController? FindPlayer()
    {
        var nodes = GetTree().GetNodesInGroup("player");
        return nodes.Count == 0 ? null : nodes[0] as PlayerController;
    }
}
