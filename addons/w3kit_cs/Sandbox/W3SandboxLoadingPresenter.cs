using System;
using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 沙盒进场时的加载遮罩：进度条 + 详情文案 + 稳定帧数淡出。
/// 从 w3_sandbox.gd 的 _begin_loading_screen/_update_loading_screen 等方法拆出，
/// 只依赖 <see cref="W3SandboxController"/> 通过构造函数传入的节点引用与回调。
/// </summary>
public sealed class W3SandboxLoadingPresenter
{
    private const int LoadingStableFrames = 45;

    private readonly Node _owner;
    private readonly Control? _overlay;
    private readonly Label? _title;
    private readonly ProgressBar? _progress;
    private readonly Label? _detail;
    private readonly Control? _hud;
    private readonly FreeFlyCamera? _cam;

    private bool _loadingFadeStarted;
    private int _stableLoadingFrames;

    public bool Loading { get; private set; } = true;
    public bool LoadSetupComplete { get; set; }

    /// <summary>加载淡出完成后触发（对应原 GDS 里 finish 末尾的 _update_hud() 等收尾）。</summary>
    public Action? OnFinished;

    public W3SandboxLoadingPresenter(Node owner, Control? overlay, Label? title, ProgressBar? progress, Label? detail, Control? hud, FreeFlyCamera? cam)
    {
        _owner = owner;
        _overlay = overlay;
        _title = title;
        _progress = progress;
        _detail = detail;
        _hud = hud;
        _cam = cam;
    }

    public void Begin()
    {
        Loading = true;
        LoadSetupComplete = false;
        _loadingFadeStarted = false;
        _stableLoadingFrames = 0;
        if (_hud != null)
            _hud.Visible = false;
        if (_overlay != null)
        {
            _overlay.Visible = true;
            _overlay.Modulate = Colors.White;
        }
        if (_cam != null)
            _cam.input_blocked = true;
        SetLoadingVisual(0.02f, "正在初始化渲染器");
    }

    public float GetLoadingProgress() => _progress != null ? (float)_progress.Value / 100.0f : 0.0f;

    public void SetLoadingVisual(float progress, string detail)
    {
        float clamped = Mathf.Clamp(progress, 0.0f, 1.0f);
        if (_progress != null)
            _progress.Value = clamped * 100.0;
        if (_detail != null)
            _detail.Text = $"{detail}  ·  {Mathf.RoundToInt(clamped * 100.0f)}%";
        if (_title != null)
            _title.Text = "正在进入失落神庙";
    }

    public void ShowLoadingError(string message)
    {
        LoadSetupComplete = false;
        if (_title != null)
            _title.Text = "地图加载失败";
        if (_detail != null)
            _detail.Text = message;
        if (_progress != null)
            _progress.Value = 0.0;
    }

    /// <summary>每帧调用一次。<paramref name="applyZoomRenderScale"/> 对应控制器的缩放/光照 LOD 应用；
    /// <paramref name="getBatcherLoadingState"/> 为 null（地形专用预览或尚无批处理器）时直接进入稳定帧计时。</summary>
    public void UpdateLoadingScreen(Action<bool> applyZoomRenderScale, Func<Godot.Collections.Dictionary?>? getBatcherLoadingState, bool terrainOnly)
    {
        if (!LoadSetupComplete || _loadingFadeStarted)
            return;
        // 用最终的动态分辨率流式传输几何体，但阴影/GI/体积雾等档位在加载队列清空前保持关闭。
        applyZoomRenderScale(false);
        Godot.Collections.Dictionary? maybeState = getBatcherLoadingState?.Invoke();
        if (maybeState == null || terrainOnly)
        {
            applyZoomRenderScale(true);
            _stableLoadingFrames++;
            UpdateWarmupProgress();
            return;
        }
        Godot.Collections.Dictionary state = maybeState;
        bool complete = state.TryGetValue("complete", out Variant completeV) && completeV.AsBool();
        if (!complete)
        {
            _stableLoadingFrames = 0;
            float progress = state.TryGetValue("progress", out Variant progressV) ? progressV.AsSingle() : 0.22f;
            string phase = state.TryGetValue("phase", out Variant phaseV) ? phaseV.AsString() : "正在加载";
            SetLoadingVisual(progress, phase);
            return;
        }
        // 流式传输完成后才启用并预热依赖相机的最终光照档位。
        applyZoomRenderScale(true);
        _stableLoadingFrames++;
        UpdateWarmupProgress();
    }

    private void UpdateWarmupProgress()
    {
        float warmup = Mathf.Clamp((float)_stableLoadingFrames / LoadingStableFrames, 0.0f, 1.0f);
        SetLoadingVisual(0.95f + warmup * 0.05f,
            $"正在稳定画面 {Mathf.Min(_stableLoadingFrames, LoadingStableFrames)}/{LoadingStableFrames}");
        if (_stableLoadingFrames >= LoadingStableFrames)
            FinishLoadingScreenAsync();
    }

    private async void FinishLoadingScreenAsync()
    {
        if (_loadingFadeStarted)
            return;
        _loadingFadeStarted = true;
        SetLoadingVisual(1.0f, "加载完成");
        await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_overlay != null)
        {
            Tween tween = _owner.CreateTween();
            tween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(_overlay, "modulate:a", 0.0, 0.35);
            await _owner.ToSignal(tween, Tween.SignalName.Finished);
            _overlay.Visible = false;
            _overlay.Modulate = Colors.White;
        }
        Loading = false;
        if (_hud != null)
            _hud.Visible = true;
        if (_cam != null)
            _cam.input_blocked = false;
        GD.Print($"[W3Sandbox] loading complete; stable_frames={_stableLoadingFrames}");
        OnFinished?.Invoke();
    }
}
