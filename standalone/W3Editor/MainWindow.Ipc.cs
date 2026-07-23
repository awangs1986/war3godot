using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using W3Editor.Services;

namespace W3Editor;

/// <summary>
/// Localhost JSON control port (default 17322), mirroring HiveWE's IPC:
/// POST / {"cmd": "ping" | "status" | "open_map" | "screenshot"}.
/// Enables background screenshots at any time without window capture or process restarts.
/// </summary>
public partial class MainWindow
{
    private EditorControlServer? _controlServer;
    private bool _ipc3dWarm;
    // Commands await (settle delays, GL frames) and would otherwise interleave
    // on the UI thread — e.g. an open_map landing mid-screenshot. Serialize.
    private readonly SemaphoreSlim _ipcGate = new(1, 1);

    private void StartControlServer()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--no-ipc"))
            return;
        int port = EditorControlServer.DefaultPort;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--ipc-port" && int.TryParse(args[i + 1], out int p))
                port = p;
        }
        try
        {
            _controlServer = new EditorControlServer(port, DispatchIpcAsync);
        }
        catch (Exception ex)
        {
            // Port already taken (second editor instance) — IPC just stays off.
            Console.Error.WriteLine($"[ipc] control server not started: {ex.Message}");
        }
    }

    private async Task<string> DispatchIpcAsync(string body)
    {
        await _ipcGate.WaitAsync();
        try
        {
            // HttpListener thread → UI thread; commands touch the session/GL view.
            return await Dispatcher.UIThread.InvokeAsync(() => DispatchIpcOnUiAsync(body));
        }
        finally
        {
            _ipcGate.Release();
        }
    }

    private async Task<string> DispatchIpcOnUiAsync(string body)
    {
        JsonNode req;
        try
        {
            req = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) ?? new JsonObject();
        }
        catch (Exception ex)
        {
            return IpcErr("invalid JSON: " + ex.Message);
        }
        string cmd = req["cmd"]?.GetValue<string>() ?? "";
        try
        {
            return cmd switch
            {
                "ping" => IpcOk(new JsonObject { ["cmd"] = "ping", ["pong"] = true, ["port"] = _controlServer?.Port }),
                "status" => IpcStatus(),
                "open_map" => await IpcOpenMapAsync(req),
                "screenshot" => await IpcScreenshotAsync(req),
                "ui_screenshot" => await IpcUiScreenshotAsync(req),
                "" => IpcErr("missing cmd"),
                _ => IpcErr($"unknown cmd: {cmd} (commands: ping, status, open_map, screenshot, ui_screenshot)"),
            };
        }
        catch (Exception ex)
        {
            return IpcErr(ex.Message);
        }
    }

    private string IpcStatus()
    {
        var j = new JsonObject
        {
            ["cmd"] = "status",
            ["port"] = _controlServer?.Port,
            ["mapLoaded"] = _session.IsOpen,
            ["view3d"] = GlView.IsVisible,
            ["commands"] = new JsonArray("ping", "status", "open_map", "screenshot", "ui_screenshot"),
        };
        if (_session.IsOpen && _session.Map is { } map)
        {
            j["mapPath"] = _session.SourcePath;
            j["terrainWidth"] = map.Width;
            j["terrainHeight"] = map.Height;
            j["doodadCount"] = _session.Doodads.Count;
            j["unitCount"] = _session.Units.Count;
            j["dirty"] = _session.IsDirty;
        }
        return IpcOk(j);
    }

    private async Task<string> IpcOpenMapAsync(JsonNode req)
    {
        string? path = req["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return IpcErr("open_map requires path");
        bool force = req["force"]?.GetValue<bool>() == true;

        // Same map already open → no-op unless forced (a reload would clear the
        // user's undo stack for nothing).
        if (_session.IsOpen && !force && PathsEqual(_session.SourcePath, path))
        {
            return IpcOk(new JsonObject
            {
                ["cmd"] = "open_map",
                ["path"] = _session.SourcePath,
                ["alreadyOpen"] = true,
                ["doodadCount"] = _session.Doodads.Count,
                ["unitCount"] = _session.Units.Count,
            });
        }
        // Never silently discard unsaved user edits.
        if (_session.IsOpen && _session.IsDirty && !force)
            return IpcErr("map has unsaved changes; save first or pass force:true to discard");

        // Dialog-free variant of OpenPath (IPC must not block on a message box).
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".w3x" or ".w3m")
        {
            var (folder, _) = await Task.Run(() => W3Editor.Services.W3xImporter.Extract(path));
            _session.Open(folder);
        }
        else if (Path.GetDirectoryName(path) is { } dir
            && Path.GetFileName(path).Equals("war3map.w3e", StringComparison.OrdinalIgnoreCase))
        {
            _session.Open(dir);
        }
        else
        {
            _session.Open(path);
        }
        _history.Clear();
        GlView.Scene.ClearPluginPreview();
        RefreshFromSession();
        Refresh3DIfActive();
        _ipc3dWarm = false;
        return IpcOk(new JsonObject
        {
            ["cmd"] = "open_map",
            ["path"] = _session.SourcePath,
            ["doodadCount"] = _session.Doodads.Count,
            ["unitCount"] = _session.Units.Count,
        });
    }

    /// <summary>
    /// Offscreen GL screenshot. Optional fields:
    ///   path (PNG), width/height (pixels, default viewport),
    ///   focusX/focusY (world units → camera target), distance, yawDeg, pitchDeg,
    ///   topdown (bool), settleMs (extra wait before capture).
    /// </summary>
    private async Task<string> IpcScreenshotAsync(JsonNode req)
    {
        if (!_session.IsOpen || _session.Map is not { } map)
            return IpcErr("no map open");

        string path = req["path"]?.GetValue<string>() is { Length: > 0 } p
            ? Path.GetFullPath(p)
            : Services.EditorPaths.OutFile($"ipc_shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        bool started3d = false;
        if (!GlView.IsVisible)
        {
            BtnView3D.IsChecked = true;
            Start3D();
            started3d = true;
        }
        // First 3D activation on this map decodes atlases + builds doodad banks.
        int settle = req["settleMs"]?.GetValue<int>() ?? (started3d || !_ipc3dWarm ? 2500 : 150);
        await Task.Delay(settle);
        _ipc3dWarm = true;

        var cam = GlView.Scene.Camera;
        if (req["topdown"]?.GetValue<bool>() == true)
        {
            cam.Target = new System.Numerics.Vector3(0f, 0f, 0f);
            cam.Yaw = MathF.PI;
            cam.Pitch = MathF.PI / 2f - 0.001f;
            // Frame the whole map: extent in world units at fov 50°.
            float extent = Math.Max(map.Width - 1, map.Height - 1) * 128f;
            cam.Distance = req["distance"]?.GetValue<float>() ?? extent * 1.15f;
        }
        else
        {
            if (req["focusX"] is { } fx && req["focusY"] is { } fy)
                cam.Target = new System.Numerics.Vector3(fx.GetValue<float>(), 0f, fy.GetValue<float>());
            if (req["distance"] is { } d)
                cam.Distance = d.GetValue<float>();
            if (req["yawDeg"] is { } yaw)
                cam.Yaw = yaw.GetValue<float>() * MathF.PI / 180f;
            if (req["pitchDeg"] is { } pitch)
                cam.Pitch = pitch.GetValue<float>() * MathF.PI / 180f;
        }
        await Task.Delay(120); // let one frame run the above-ground clamp

        // Clamp: width*height*4 is computed in int downstream and huge values
        // would also blow past renderbuffer limits.
        int width = Math.Clamp(req["width"]?.GetValue<int>() ?? 0, 0, 8192);
        int height = Math.Clamp(req["height"]?.GetValue<int>() ?? 0, 0, 8192);
        string? error = await GlView.CaptureToPngAsync(path, width, height);
        if (error != null)
            return IpcErr("capture failed: " + error);
        return IpcOk(new JsonObject
        {
            ["cmd"] = "screenshot",
            ["path"] = path,
            ["statusLine"] = GlView.Scene.StatusLine,
            ["camera"] = new JsonObject
            {
                ["targetX"] = cam.Target.X,
                ["targetY"] = cam.Target.Z,
                ["distance"] = cam.Distance,
                ["yawDeg"] = cam.Yaw * 180f / MathF.PI,
                ["pitchDeg"] = cam.Pitch * 180f / MathF.PI,
            },
        });
    }

    /// <summary>
    /// Whole-window capture (PrintWindow) for verifying UI states — palettes,
    /// dialogs, status bar. Optional: tab (right tool-tab index), settleMs.
    /// The GL viewport itself is better served by "screenshot" (offscreen).
    /// </summary>
    private async Task<string> IpcUiScreenshotAsync(JsonNode req)
    {
        string path = req["path"]?.GetValue<string>() is { Length: > 0 } p
            ? Path.GetFullPath(p)
            : Services.EditorPaths.OutFile($"ipc_ui_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        if (req["tab"] is { } tab)
            ToolTabs.SelectedIndex = tab.GetValue<int>();
        await Task.Delay(req["settleMs"]?.GetValue<int>() ?? 600);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (!OperatingSystem.IsWindows() || !Services.NativeCapture.CaptureWindow(hwnd, path))
            return IpcErr("PrintWindow capture failed");
        return IpcOk(new JsonObject { ["cmd"] = "ui_screenshot", ["path"] = path });
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string IpcOk(JsonObject j)
    {
        j["ok"] = true;
        return j.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string IpcErr(string message) =>
        new JsonObject { ["ok"] = false, ["error"] = message }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
