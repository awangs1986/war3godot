using System.Collections.Generic;
using Diablo1Like.W3.Lighting;
using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 沙盒 HUD：状态文本、动态创建的热键条与 Bistro 场景切换条。
/// 从 w3_sandbox.gd 的 _update_hud/_refresh_hotkeys_bar/_ensure_bistro_bar 等方法拆出。
/// 只读取 <see cref="W3SandboxController"/> 的状态，不修改（除 Bistro 按钮回调外）。
/// </summary>
public sealed class W3SandboxHud
{
    private readonly W3SandboxController _c;
    private readonly CanvasLayer? _layer;
    private readonly Label? _hud;

    private Label? _hotkeysBar;
    private PanelContainer? _bistroBar;
    private readonly Dictionary<int, Button> _bistroBtns = new();

    public W3SandboxHud(W3SandboxController controller, CanvasLayer? layer, Label? hud)
    {
        _c = controller;
        _layer = layer;
        _hud = hud;
    }

    public void UpdateHud()
    {
        RefreshHotkeysBar();
        if (_hud == null)
            return;
        bool fogState = _c.Settings != null && _c.Settings.Get("fog_enabled").AsBool();
        int waterN = 0;
        if (_c.World?.water != null)
            waterN = _c.World.water.GetWaterTileCount();
        string previewPerf = $"3D {Mathf.RoundToInt(_c.RenderScale * 100.0f)}% | static 0/0 | anim 0/0";
        if (_c.World?.batcher != null)
        {
            Godot.Collections.Dictionary counts = _c.World.batcher.get_counts();
            previewPerf = $"LOD{DictGet(counts, "lod", 0)} | 3D {Mathf.RoundToInt(_c.RenderScale * 100.0f)}% | " +
                          $"static {DictGet(counts, "visible_static", 0)}/{DictGet(counts, "ok", 0)} | " +
                          $"anim {DictGet(counts, "anim_active", 0)}/{DictGet(counts, "animated", 0)}";
        }
        string selectionStatus = "LMB=select visible unit";
        if (_c.UnitSelector != null)
            selectionStatus = $"LMB=select unit | {_c.UnitSelector.GetLastSummary()}";

        if (!_c.DebugTools)
        {
            _hud.Text = $"{_c.World?.get_status()}\n" +
                        $"天气={_c.Weather} F=fog({(fogState ? "开" : "关")}) `=/sun /fog | wheel=zoom | {selectionStatus}\n" +
                        $"water={waterN} | {previewPerf}";
            return;
        }

        string gridState = "OFF";
        string hover = "—";
        if (_c.TileDebug != null)
        {
            gridState = _c.TileDebug.is_debug_enabled() ? "ON" : "OFF";
            if (_c.TileDebug.is_debug_enabled())
            {
                Vector2I ht = _c.TileDebug.get_hover_tile();
                if (ht.X >= 0)
                    hover = $"{ht.X},{ht.Y}";
            }
        }
        string heroState = "T=出剑圣";
        if (_c.Game != null && _c.Game.has_live_player())
        {
            var p = _c.Game.player!;
            heroState = $"剑圣HP {(int)p.hp}/{(int)p.max_hp} LMB=移动/攻击 T=传送";
        }
        string focusText = _c.FocusedTile.X < 0 ? "—" : $"{_c.FocusedTile.X},{_c.FocusedTile.Y}";
        _hud.Text = $"{_c.World?.get_status()}\n" +
                    $"[debug] F=fog({(fogState ? "开" : "关")}) G=grid({gridState}) Tab=inspect({(_c.InspectMode ? "ON" : "OFF")}) " +
                    $"H=soft({(_c.SoftTint ? "ON" : "OFF")}) {heroState} `=goto | {selectionStatus}\n" +
                    $"{previewPerf} | water={waterN} | look={hover} | FOCUS={focusText}";
    }

    public void EnsureHotkeysBar()
    {
        if (_hotkeysBar != null)
        {
            RefreshHotkeysBar();
            return;
        }
        if (_layer == null)
            return;
        var label = new Label
        {
            Name = "HotkeysBar",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        label.OffsetLeft = 12.0f;
        label.OffsetTop = 4.0f;
        label.OffsetRight = -12.0f;
        label.OffsetBottom = 40.0f;
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 0.86f, 0.95f));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);
        _layer.AddChild(label);
        _hotkeysBar = label;
        // 状态文字保持在热键条下方。
        if (_hud != null)
        {
            _hud.OffsetTop = 44.0f;
            _hud.OffsetBottom = 200.0f;
        }
        RefreshHotkeysBar();
    }

    private void RefreshHotkeysBar()
    {
        if (_hotkeysBar == null)
            return;
        bool fogOn = _c.Settings != null && _c.Settings.Get("fog_enabled").AsBool();
        bool orbOn = _c.LightOrb is { ProbeEnabled: true };
        var parts = new List<string>
        {
            $"F 雾({(fogOn ? "开" : "关")})",
            $"L 点光源({(orbOn ? "开" : "关")})",
            "` 控制台",
        };
        if (_c.DiabloPlay)
        {
            parts.Add("左键点地移动");
            parts.Add("play=ON");
        }
        else
        {
            parts.AddRange(new[] { "WASD 移动", "QE 升降", "右键 视角", "滚轮 缩放" });
        }
        if (_c.DebugTools)
        {
            parts.Add("左键点选单位");
            parts.Add("选中#—");
            parts.Add($"N 全部名牌({(_c.UnitLabelsOn() ? "开" : "关")})");
            bool gridOn = _c.TileDebug != null && _c.TileDebug.is_debug_enabled();
            parts.Add($"G 格子({(gridOn ? "开" : "关")})");
            parts.Add($"Tab 检视({(_c.InspectMode ? "开" : "关")})");
            parts.Add($"H 软染色({(_c.SoftTint ? "开" : "关")})");
            // 剑圣指挥模式底层挂钩尚未实现（EnsureSpawnPawn 空实现），固定显示"关"。
            parts.Add("M 剑圣(关)");
        }
        _hotkeysBar.Text = string.Join("  ·  ", parts);
    }

    public void EnsureBistroBar()
    {
        if (_bistroBar != null)
        {
            RefreshBistroBar();
            return;
        }
        if (_layer == null)
            return;
        var panel = new PanelContainer { Name = "BistroLitBar" };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -420.0f;
        panel.OffsetTop = 48.0f;
        panel.OffsetRight = -12.0f;
        panel.OffsetBottom = 92.0f;
        _layer.AddChild(panel);
        _bistroBar = panel;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        panel.AddChild(vbox);

        var title = new Label { Text = "Bistro lit", HorizontalAlignment = HorizontalAlignment.Right };
        vbox.AddChild(title);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(row);

        (int scenario, string label)[] scenarios =
        {
            (0, "Noon"), (1, "Afternoon"), (2, "Dusk"), (3, "Night"),
        };
        foreach ((int scenario, string labelText) in scenarios)
        {
            var btn = new Button { Text = labelText, FocusMode = Control.FocusModeEnum.None };
            btn.Pressed += () => OnBistroScenarioPressed(scenario);
            row.AddChild(btn);
            _bistroBtns[scenario] = btn;
        }

        RefreshBistroBar();
    }

    private void OnBistroScenarioPressed(int scenario)
    {
        // 切换场景总是激活 Bistro lit（独立于 War3 / UE 选择）。
        if (!W3LightingRouter.UsesBistroLit(_c.LitSettings))
        {
            var loaded = GD.Load<Resource>("res://addons/w3kit/bistro_lit_default.tres");
            _c.LitSettings = loaded != null ? (Resource)loaded.Duplicate(true) : new BistroLitSettings();
        }
        _c.LitSettings ??= new BistroLitSettings();
        _c.LitSettings.Set("scenario", scenario);
        _c.LitSettings.Set("manual_override", false);
        // 户外可读性：柔和阳光阴影。
        if (_c.World != null && _c.LitSettings.Get("sun_shadows").AsBool())
            _c.World.preview_no_shadows = false;
        _c.ApplyLighting();
        RefreshBistroBar();
        UpdateHud();
        GD.Print("[W3Sandbox] Bistro scenario=", scenario, " → ", W3LightingRouter.LitLabel(_c.Settings));
    }

    private void RefreshBistroBar()
    {
        if (_bistroBar == null)
            return;
        int active = -1;
        Resource? effective = _c.Settings ?? _c.LitSettings;
        if (W3LightingRouter.UsesBistroLit(effective))
        {
            Variant scenarioVariant = effective!.Get("scenario");
            active = scenarioVariant.VariantType == Variant.Type.Nil ? 1 : scenarioVariant.AsInt32();
        }
        foreach ((int scenario, Button btn) in _bistroBtns)
        {
            if (btn == null)
                continue;
            btn.Disabled = false;
            btn.Modulate = scenario == active ? new Color(1.2f, 1.15f, 0.9f) : Colors.White;
            btn.ButtonPressed = scenario == active;
        }
    }

    private static int DictGet(Godot.Collections.Dictionary dict, string key, int fallback) =>
        dict.TryGetValue(key, out Variant value) ? value.AsInt32() : fallback;
}
