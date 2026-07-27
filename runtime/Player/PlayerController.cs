using Diablo1Like.Runtime.Autoload;
using Diablo1Like.Runtime.Enemy;
using Godot;

namespace Diablo1Like.Runtime.Player;

/// <summary>
/// 点击移动（导航 + 直线兜底）+ 相机相对 WASD 备用移动。
/// 迁移自 scripts/player.gd。
/// </summary>
[GlobalClass]
public partial class PlayerController : CharacterBody3D
{
    [Signal]
    public delegate void DiedEventHandler();

    private const float Speed = 7.5f;
    private const float AttackRange = 2.2f;
    private const float AttackCooldown = 0.45f;
    private const float ArriveDist = 0.5f;

    public Node3D? AttackTarget;

    private float _attackCd;
    private bool _alive = true;
    private bool _navReady;
    private Vector3 _moveGoal = Vector3.Zero;
    private bool _hasMoveGoal;

    private MeshInstance3D _mesh = null!;
    private NavigationAgent3D _agent = null!;
    private GameState _gameState = null!;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _gameState = GetNode<GameState>("/root/GameState");

        AddToGroup("player");
        CollisionLayer = 2;
        CollisionMask = 1;
        FloorSnapLength = 0.3f;
        _agent.PathDesiredDistance = 0.5f;
        _agent.TargetDesiredDistance = 0.6f;
        _agent.AvoidanceEnabled = false;
        _agent.Radius = 0.45f;
        _agent.Height = 1.6f;
        _gameState.HealFull();
        _gameState.PlayerStatsChanged += OnStats;
        OnStats();
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
        _navReady = true;
        if (_hasMoveGoal)
            _agent.TargetPosition = _moveGoal;
    }

    private void OnStats()
    {
        if (_mesh.MaterialOverride == null)
            _mesh.MaterialOverride = new StandardMaterial3D();
        var mat = (StandardMaterial3D)_mesh.MaterialOverride;
        float t = _gameState.MaxHp <= 0 ? 1.0f : (float)_gameState.Hp / _gameState.MaxHp;
        mat.AlbedoColor = new Color(0.25f + 0.55f * t, 0.35f * t, 0.85f);
    }

    private void FaceDir(Vector3 dir)
    {
        var flat = new Vector3(dir.X, 0.0f, dir.Z);
        if (flat.Length() < 0.001f)
            return;
        flat = flat.Normalized();
        // 只旋转 Yaw —— 完整 look_at 会让胶囊体倾斜并卡死移动。
        Rotation = new Vector3(Rotation.X, Mathf.Atan2(flat.X, flat.Z), Rotation.Z);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_alive)
            return;
        float dt = (float)delta;
        _attackCd = Mathf.Max(0.0f, _attackCd - dt);

        if (!IsOnFloor())
            Velocity = new Vector3(Velocity.X, Velocity.Y - 20.0f * dt, Velocity.Z);
        else
            Velocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);

        Vector3 wasd = WasdDir();
        if (wasd.Length() > 0.01f)
        {
            ClearMoveGoal();
            AttackTarget = null;
            Velocity = new Vector3(wasd.X * Speed, Velocity.Y, wasd.Z * Speed);
            FaceDir(wasd);
            MoveAndSlide();
            return;
        }

        if (AttackTarget != null && IsInstanceValid(AttackTarget))
        {
            Vector3 toEnemy = AttackTarget.GlobalPosition - GlobalPosition;
            toEnemy.Y = 0.0f;
            if (toEnemy.Length() <= AttackRange)
            {
                ClearMoveGoal();
                Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
                if (toEnemy.Length() > 0.01f)
                    FaceDir(toEnemy);
                TryAttack();
                MoveAndSlide();
                return;
            }
            SetMove(AttackTarget.GlobalPosition);
        }

        if (_hasMoveGoal)
            SteerTowardGoal();
        else
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);

        MoveAndSlide();
    }

    private static Vector3 WasdDir()
    {
        var input = new Vector2(
            Input.GetActionStrength("ui_right") - Input.GetActionStrength("ui_left"),
            Input.GetActionStrength("ui_down") - Input.GetActionStrength("ui_up"));
        if (Input.IsKeyPressed(Key.D))
            input.X += 1.0f;
        if (Input.IsKeyPressed(Key.A))
            input.X -= 1.0f;
        if (Input.IsKeyPressed(Key.S))
            input.Y += 1.0f;
        if (Input.IsKeyPressed(Key.W))
            input.Y -= 1.0f;
        if (input.Length() < 0.01f)
            return Vector3.Zero;
        input = input.Normalized();
        // 匹配 45° 等距相机：屏幕上/屏幕右对应到 XZ 平面。
        Vector3 forward = new Vector3(-1.0f, 0.0f, -1.0f).Normalized();
        Vector3 right = new Vector3(1.0f, 0.0f, -1.0f).Normalized();
        return (right * input.X + forward * -input.Y).Normalized();
    }

    private void SteerTowardGoal()
    {
        var flatGoal = new Vector3(_moveGoal.X, GlobalPosition.Y, _moveGoal.Z);
        Vector3 toGoal = flatGoal - GlobalPosition;
        toGoal.Y = 0.0f;
        if (toGoal.Length() <= ArriveDist)
        {
            ClearMoveGoal();
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            return;
        }

        Vector3 dir = toGoal;
        if (_navReady)
        {
            _agent.TargetPosition = flatGoal;
            if (!_agent.IsNavigationFinished())
            {
                Vector3 next = _agent.GetNextPathPosition();
                Vector3 navDir = GlobalPosition.DirectionTo(next);
                navDir.Y = 0.0f;
                if (navDir.Length() > 0.05f)
                    dir = navDir;
            }
        }

        if (dir.Length() < 0.001f)
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            return;
        }
        dir = dir.Normalized();
        Velocity = new Vector3(dir.X * Speed, Velocity.Y, dir.Z * Speed);
        FaceDir(dir);
    }

    public void HandleWorldClick(Vector3 worldPos, Node? clicked)
    {
        if (!_alive)
            return;
        if (clicked != null && clicked.IsInGroup("enemy") && clicked is Node3D clickedNode)
        {
            AttackTarget = clickedNode;
            SetMove(clickedNode.GlobalPosition);
            return;
        }
        AttackTarget = null;
        SetMove(worldPos);
    }

    private void SetMove(Vector3 pos)
    {
        _moveGoal = new Vector3(pos.X, GlobalPosition.Y, pos.Z);
        _hasMoveGoal = true;
        if (_navReady)
            _agent.TargetPosition = _moveGoal;
    }

    private void ClearMoveGoal()
    {
        _hasMoveGoal = false;
        if (_navReady)
            _agent.TargetPosition = GlobalPosition;
    }

    private void TryAttack()
    {
        if (_attackCd > 0.0f || AttackTarget == null)
            return;
        _attackCd = AttackCooldown;
        if (AttackTarget is EnemyController enemy)
            enemy.TakeDamage(_gameState.GetAttackDamage());
        Tween tw = CreateTween();
        tw.TweenProperty(_mesh, "scale", new Vector3(1.15f, 0.9f, 1.15f), 0.06);
        tw.TweenProperty(_mesh, "scale", Vector3.One, 0.08);
    }

    public void TakeDamage(int amount)
    {
        if (!_alive)
            return;
        _gameState.ApplyDamage(amount);
        if (_gameState.Hp <= 0)
        {
            _alive = false;
            EmitSignal(SignalName.Died);
        }
    }

    public bool IsAlive() => _alive;
}
