using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// `（波浪键）控制台的命令解析：/debug /sun /fog 以及 "x,y" 顶视跳转。
/// 从 w3_sandbox.gd 的 _on_console_submit 拆出。
/// </summary>
public sealed class W3SandboxDebugConsole
{
    // 顶视正交视角下约等于 3 格宽。
    private const float TileOrtho = 420.0f;

    private readonly W3SandboxController _c;
    private readonly LineEdit? _edit;

    public W3SandboxDebugConsole(W3SandboxController controller, LineEdit? edit)
    {
        _c = controller;
        _edit = edit;
    }

    public void OnSubmit(string text)
    {
        string t = text.StripEdges();
        if (t.Length == 0)
            return;
        string cmd = t.ToLowerInvariant().Replace(" ", "");
        if (cmd is "/debug" or "debug")
        {
            _c.SetDebugTools(!_c.DebugTools);
            CloseAfterCommand();
            return;
        }
        if (cmd is "/debugon" or "/debug=1")
        {
            _c.SetDebugTools(true);
            CloseAfterCommand();
            return;
        }
        if (cmd is "/debugoff" or "/debug=0")
        {
            _c.SetDebugTools(false);
            CloseAfterCommand();
            return;
        }
        if (cmd == "/sun")
        {
            // 正午硬阳光；再次输入恢复默认观感。
            _c.Weather = _c.Weather == "sun" ? "default" : "sun";
            _c.FogOn = false;
            _c.ApplyLighting();
            GD.Print("[W3Sandbox] weather=", _c.Weather);
            CloseAfterCommand();
            _c.UpdateHud();
            return;
        }
        if (cmd == "/fog")
        {
            // 柔和阴天 + 远雾（近处保持清晰）；再次输入关闭。
            bool on = _c.Weather != "overcast";
            _c.Weather = on ? "overcast" : "default";
            _c.FogOn = on;
            _c.ApplyLighting();
            GD.Print("[W3Sandbox] weather=", _c.Weather);
            CloseAfterCommand();
            _c.UpdateHud();
            return;
        }

        t = t.Replace("，", ",").Replace(" ", "");
        string[] parts = t.Split(',');
        if (parts.Length < 2)
        {
            SetPlaceholder("x,y 或 /debug /sun /fog");
            _edit?.Clear();
            return;
        }
        if (!parts[0].IsValidInt() || !parts[1].IsValidInt())
        {
            SetPlaceholder("坐标必须是整数");
            _edit?.Clear();
            return;
        }
        if (!_c.DebugTools)
        {
            SetPlaceholder("先输入 /debug");
            _edit?.Clear();
            return;
        }
        int tx = parts[0].ToInt();
        int ty = parts[1].ToInt();
        if (_c.World?.map == null || _c.World.sampler == null)
            return;
        int maxX = _c.World.map.Call("tile_count_x").AsInt32() - 1;
        int maxY = _c.World.map.Call("tile_count_y").AsInt32() - 1;
        tx = Mathf.Clamp(tx, 0, maxX);
        ty = Mathf.Clamp(ty, 0, maxY);
        Vector3 center = _c.World.sampler.Call("tile_center_world", new Vector2I(tx, ty)).AsVector3();
        _c.FocusedTile = new Vector2I(tx, ty);
        _c.EnsureTileDebug();
        _c.TileDebug?.highlight_tile(_c.FocusedTile);
        if (_c.Cam != null)
            _c.Cam.focus_tile_topdown(center, TileOrtho);
        _c.SetDirLegendVisible(true);
        GD.Print($"[W3Sandbox] goto tile {tx},{ty} (topdown) -> {center}");
        _c.SetConsoleOpen(false);
        _c.UpdateHud();
    }

    private void CloseAfterCommand()
    {
        _edit?.Clear();
        _c.SetConsoleOpen(false);
    }

    private void SetPlaceholder(string text)
    {
        if (_edit != null)
            _edit.PlaceholderText = text;
    }
}
