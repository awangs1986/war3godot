using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HiveWE.Mcp.Tools;

[McpServerToolType]
public sealed class HiveWeTools(HiveWeSession session)
{
    [McpServerTool(Name = "hivewe_status"), Description(
        "HiveWE MCP status: exe/source paths, whether a process is running, last opened map.")]
    public string Status() => session.StatusJson();

    [McpServerTool(Name = "hivewe_configure"), Description(
        "Override default HiveWE.exe and/or source root paths for this MCP session.")]
    public string Configure(
        [Description("Absolute path to HiveWE.exe")] string? exePath = null,
        [Description("Absolute path to HiveWE source root")] string? sourceRoot = null)
    {
        session.Configure(exePath, sourceRoot);
        return session.StatusJson();
    }

    [McpServerTool(Name = "hivewe_launch"), Description(
        "Start HiveWE.exe (cwd = exe folder so data/ resolves). Optional --map <folder>.")]
    public string Launch(
        [Description("Map folder (extracted war3map.*) or path passed after --map")] string? mapPath = null,
        [Description("Kill previous HiveWE we started before launching")] bool restart = true)
        => session.Launch(mapPath, restart);

    [McpServerTool(Name = "hivewe_open_map"), Description(
        "Launch (or relaunch) HiveWE with --map pointing at an extracted map folder.")]
    public string OpenMap(
        [Description("Absolute path to map folder containing war3map.w3e")] string mapPath)
        => session.Launch(mapPath, restart: true);

    [McpServerTool(Name = "hivewe_close"), Description("Close the HiveWE process started by this MCP (or force-kill).")]
    public string Close(
        [Description("Force kill if CloseMainWindow fails")] bool force = false)
        => session.Close(force);

    [McpServerTool(Name = "hivewe_screenshot"), Description(
        "Capture the HiveWE main window to PNG (HIVEWE_OUT_DIR or %TEMP%\\hivewe) and return path + JPEG thumbnail.")]
    public async Task<CallToolResult> Screenshot(
        [Description("Optional absolute PNG path")] string? outputPath = null,
        [Description("Max wait for window (ms), default 15000 — CASC/map load can be slow")] int waitMs = 15000,
        CancellationToken cancellationToken = default)
    {
        // Ensure something is running.
        if (session.Process is null or { HasExited: true })
            session.Launch(session.LastMapPath, restart: false);

        string png = await session.ScreenshotAsync(outputPath, waitMs, cancellationToken);
        var content = new List<ContentBlock>
        {
            new TextContentBlock { Text = $"screenshot={png}" },
        };
        try
        {
            content.Add(new ImageContentBlock
            {
                MimeType = "image/jpeg",
                Data = MakeJpegThumbnail(png, 1280, 75),
            });
        }
        catch (Exception ex)
        {
            content.Add(new TextContentBlock { Text = "thumbnail skipped: " + ex.Message });
        }
        return new CallToolResult { Content = content };
    }

    [McpServerTool(Name = "hivewe_source_search"), Description(
        "Regex search under HiveWE source root (skips build/.git). For algorithm cross-checks vs W3Editor.")]
    public string SourceSearch(
        [Description("Regex pattern, e.g. cliff_operation|GroundHeight")] string pattern,
        [Description("Optional single glob like *.ixx")] string? glob = null,
        [Description("Max hits 1–100")] int maxHits = 40)
        => session.SourceSearch(pattern, glob, maxHits);

    [McpServerTool(Name = "hivewe_source_read"), Description(
        "Read a slice of a HiveWE source file (relative to source root or absolute under it).")]
    public string SourceRead(
        [Description("Relative path e.g. src/main.cpp or absolute under source root")] string path,
        [Description("1-based start line")] int startLine = 1,
        [Description("Number of lines (1–400)")] int lineCount = 80)
        => session.SourceRead(path, startLine, lineCount);

    [McpServerTool(Name = "hivewe_source_tree"), Description("List files/dirs under HiveWE source (shallow).")]
    public string SourceTree(
        [Description("Relative subfolder, default root")] string? relative = "src",
        [Description("Depth 1–4")] int depth = 2,
        [Description("Max entries")] int maxEntries = 80)
        => session.ListSourceTree(relative, depth, maxEntries);

    [McpServerTool(Name = "hivewe_ipc_ping"), Description(
        "Ping HiveWE localhost JSON control port (default 127.0.0.1:17321).")]
    public async Task<string> IpcPing(CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcPingAsync(cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_status"), Description(
        "Query HiveWE IPC status: map loaded, size, doodad/unit counts.")]
    public async Task<string> IpcStatus(CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcStatusAsync(cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_new_map"), Description(
        "Create a blank Lordaeron Summer map in HiveWE (temp folder). tiles: 32–480, multiple of 4. Default 160 (Lost Temple size).")]
    public async Task<string> IpcNewMap(
        [Description("Map size in tiles (default 160)")] int tiles = 160,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcAsync(new { cmd = "new_map", tiles }, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_open_map"), Description(
        "Open an extracted map folder inside a running HiveWE via IPC (no process restart). " +
        "Refuses D:\\maps\\LostTemple (use a Built/materialized folder instead).")]
    public async Task<string> IpcOpenMap(
        [Description("Absolute path to map folder containing war3map.w3e")] string mapPath,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcOpenMapAsync(mapPath, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_save"), Description("Save the current map via HiveWE IPC.")]
    public async Task<string> IpcSave(
        [Description("Optional absolute folder path; omit to save in place")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcSaveAsync(path, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_brush_stamp"), Description(
        "Stamp terrain once via IPC. Coords are HiveWE tile space (same as mouse_world). " +
        "op: raise|lower|plateau|smooth|texture|cliff_raise|cliff_lower|cliff_level|add_water|remove_water.")]
    public async Task<string> IpcBrushStamp(
        [Description("Operator name")] string op,
        [Description("Center X in tile units")] float cx,
        [Description("Center Y in tile units")] float cy,
        [Description("Brush size (palette units, default 8)")] int size = 8,
        [Description("Strength / frame_delta for height ops (default 1)")] double strength = 1,
        [Description("Tile id for texture paint, e.g. Ldrt")] string? tileId = null,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcBrushStampAsync(op, cx, cy, size, strength, tileId, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_place_doodad"), Description(
        "Place a doodad/destructible via IPC. x/y in HiveWE tile space.")]
    public async Task<string> IpcPlaceDoodad(
        [Description("Four-letter object id, e.g. LTlt")] string id,
        [Description("X in tile units")] float x,
        [Description("Y in tile units")] float y,
        [Description("Variation index")] int variation = 0,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcPlaceDoodadAsync(id, x, y, variation, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_place_unit"), Description(
        "Place a unit via IPC. x/y in HiveWE tile space.")]
    public async Task<string> IpcPlaceUnit(
        [Description("Four-letter unit id, e.g. hfoo")] string id,
        [Description("X in tile units")] float x,
        [Description("Y in tile units")] float y,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcPlaceUnitAsync(id, x, y, cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_screenshot"), Description(
        "Built-in background screenshot via IPC: HiveWE re-renders the GL scene into an offscreen framebuffer " +
        "(grabFramebuffer), so it works with the window unfocused/behind other windows — no window capture. " +
        "Optional camera placement in HiveWE tile units + radians. Returns PNG path plus embedded thumbnail.")]
    public async Task<ModelContextProtocol.Protocol.CallToolResult> IpcScreenshot(
        [Description("Absolute PNG output path; default (HIVEWE_OUT_DIR or %TEMP%\\hivewe)\\ipc_shot_*.png")] string? outputPath = null,
        [Description("Camera X in tile units (map corner grid)")] float? x = null,
        [Description("Camera Y in tile units")] float? y = null,
        [Description("Camera distance in tile units (default keeps current, HiveWE default 20)")] float? distance = null,
        [Description("Horizontal angle in radians (0 = north)")] float? horizontalAngle = null,
        [Description("Vertical angle in radians (HiveWE default -0.977)")] float? verticalAngle = null,
        CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        var payload = new Dictionary<string, object?> { ["cmd"] = "screenshot" };
        if (outputPath is { Length: > 0 }) payload["path"] = Path.GetFullPath(outputPath);
        if (x is { } px) payload["x"] = px;
        if (y is { } py) payload["y"] = py;
        if (distance is { } d) payload["distance"] = d;
        if (horizontalAngle is { } h) payload["horizontal_angle"] = h;
        if (verticalAngle is { } v) payload["vertical_angle"] = v;
        string json = await session.IpcAsync(payload, cancellationToken);

        var content = new List<ModelContextProtocol.Protocol.ContentBlock>
        {
            new ModelContextProtocol.Protocol.TextContentBlock { Text = json },
        };
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("path", out var pathProp)
                && pathProp.GetString() is { Length: > 0 } png && File.Exists(png))
            {
                content.Add(new ModelContextProtocol.Protocol.ImageContentBlock
                {
                    MimeType = "image/jpeg",
                    Data = MakeJpegThumbnail(png, maxEdge: 1280, quality: 75),
                });
            }
        }
        catch (Exception ex)
        {
            content.Add(new ModelContextProtocol.Protocol.TextContentBlock { Text = $"thumbnail skipped: {ex.Message}" });
        }
        return new ModelContextProtocol.Protocol.CallToolResult { Content = content };
    }

    [McpServerTool(Name = "hivewe_ipc_undo"), Description("Undo last HiveWE edit via IPC.")]
    public async Task<string> IpcUndo(CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcUndoAsync(cancellationToken);
    }

    [McpServerTool(Name = "hivewe_ipc_redo"), Description("Redo via IPC.")]
    public async Task<string> IpcRedo(CancellationToken cancellationToken = default)
    {
        await session.EnsureIpcReadyAsync(cancellationToken);
        return await session.IpcRedoAsync(cancellationToken);
    }

    private static byte[] MakeJpegThumbnail(string pngPath, int maxEdge, int quality)
    {
        using var input = File.OpenRead(pngPath);
        using var bitmap = SkiaSharp.SKBitmap.Decode(input)
            ?? throw new InvalidOperationException("decode failed");
        float scale = Math.Min(1f, maxEdge / (float)Math.Max(bitmap.Width, bitmap.Height));
        int tw = Math.Max(1, (int)(bitmap.Width * scale));
        int th = Math.Max(1, (int)(bitmap.Height * scale));
        SkiaSharp.SKBitmap? resized = bitmap.Resize(new SkiaSharp.SKImageInfo(tw, th), SkiaSharp.SKFilterQuality.Medium);
        try
        {
            using var image = SkiaSharp.SKImage.FromBitmap(resized ?? bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, quality)
                ?? throw new InvalidOperationException("encode failed");
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose(); // bitmap itself is owned by the outer using
        }
    }
}
