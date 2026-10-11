using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using W3Editor.Services;

namespace W3Editor.Mcp.Tools;

[McpServerToolType]
public sealed class CaptureTools(EditorSession session)
{
    [McpServerTool(Name = "screenshot_3d"), Description(
        "Capture a 3D screenshot of the current map. Uses the resident W3Editor control port (fast, background-safe, " +
        "offscreen FBO) when one is running; otherwise spawns W3Editor once. Returns PNG path plus embedded thumbnail.")]
    public async Task<CallToolResult> Screenshot3d(
        [Description("Optional absolute PNG output path; default %TEMP%\\w3editor\\shot_*.png (override via W3EDITOR_OUT_DIR)")] string? outputPath = null,
        [Description("Top-down view of the whole map (default: perspective from the map center)")] bool topdown = false,
        CancellationToken cancellationToken = default)
        => await session.LockedAsync(() => Screenshot3dCore(outputPath, topdown, cancellationToken), cancellationToken);

    private async Task<CallToolResult> Screenshot3dCore(
        string? outputPath, bool topdown, CancellationToken cancellationToken)
    {
        // Fast path: resident editor with control port.
        if (await EditorIpcClient.IsAliveAsync(cancellationToken))
        {
            bool mcpWroteDisk = session.Map.IsOpen && session.Map.IsDirty;
            if (mcpWroteDisk)
                session.Map.Save();
            if (session.Map.IsOpen)
            {
                // Point the resident editor at this session's on-disk state.
                // force only when this session actually changed the files —
                // the editor refuses to discard its own unsaved edits, and a
                // same-path clean open is a cheap no-op on the editor side.
                JsonNode? opened = await EditorIpcClient.TryPostAsync(
                    new { cmd = "open_map", path = session.Map.SourcePath, force = mcpWroteDisk }, cancellationToken);
                if (opened?["ok"]?.GetValue<bool>() != true)
                    throw new InvalidOperationException(
                        "resident editor open_map failed: " + opened?["error"]
                        + " (the editor may hold unsaved edits; save there or retry)");
            }
            string png = outputPath is { Length: > 0 }
                ? Path.GetFullPath(outputPath)
                : EditorPaths.OutFile($"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            JsonNode? shot = await EditorIpcClient.TryPostAsync(
                new { cmd = "screenshot", path = png, topdown, width = 1600, height = 900 }, cancellationToken);
            if (shot?["ok"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("resident editor screenshot failed: " + shot?["error"]);
            return BuildResult(png, $"screenshot={png} (resident IPC)\n{shot["statusLine"]}");
        }

        // Slow path: spawn a one-shot editor process.
        string spawned = await session.Screenshot3dAsync(outputPath, cancellationToken);
        string logPath = Path.ChangeExtension(spawned, ".txt");
        string log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
        return BuildResult(spawned, $"screenshot={spawned} (spawned process)\n{log}".Trim());
    }

    [McpServerTool(Name = "editor_launch"), Description(
        "Start a resident W3Editor with the localhost control port (17322) so later screenshots are instant and " +
        "background-safe. Opens the given map folder (default: this session's map).")]
    public async Task<string> EditorLaunch(
        [Description("Map folder to open (default: current MCP session map)")] string? mapPath = null,
        CancellationToken cancellationToken = default)
    {
        if (await EditorIpcClient.IsAliveAsync(cancellationToken))
            return $"already running at {EditorIpcClient.BaseUrl}";
        string path = mapPath
            ?? session.Locked(() => session.Map.IsOpen
                ? session.Map.SourcePath
                : throw new InvalidOperationException("no map open; pass mapPath"));
        int pid = await EditorIpcClient.LaunchResidentAsync(path, cancellationToken);
        return $"launched pid={pid}, control port {EditorIpcClient.BaseUrl}, map={path}";
    }

    [McpServerTool(Name = "editor_screenshot"), Description(
        "Background screenshot from the resident W3Editor (requires editor_launch or a manually started editor). " +
        "Offscreen FBO render — no window capture, works while the editor is behind other windows. " +
        "Camera: topdown for the whole map, or focusX/focusY (world units) + distance + pitchDeg/yawDeg.")]
    public async Task<CallToolResult> EditorScreenshot(
        [Description("Absolute PNG output path; default %TEMP%\\w3editor\\ipc_shot_*.png (override via W3EDITOR_OUT_DIR)")] string? outputPath = null,
        [Description("Straight-down view framing the whole map")] bool topdown = false,
        [Description("Camera target X in world units")] float? focusX = null,
        [Description("Camera target Y in world units")] float? focusY = null,
        [Description("Eye-to-target distance in world units (e.g. 2200)")] float? distance = null,
        [Description("Pitch above horizon in degrees (default keeps current)")] float? pitchDeg = null,
        [Description("Yaw in degrees, 180 = north (default keeps current)")] float? yawDeg = null,
        [Description("Output width in pixels (default 1600)")] int width = 1600,
        [Description("Output height in pixels (default 900)")] int height = 900,
        CancellationToken cancellationToken = default)
    {
        string png = outputPath is { Length: > 0 }
            ? Path.GetFullPath(outputPath)
            : EditorPaths.OutFile($"ipc_shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "screenshot",
            ["path"] = png,
            ["topdown"] = topdown,
            ["width"] = width,
            ["height"] = height,
        };
        if (focusX is { } fx) payload["focusX"] = fx;
        if (focusY is { } fy) payload["focusY"] = fy;
        if (distance is { } d) payload["distance"] = d;
        if (pitchDeg is { } p) payload["pitchDeg"] = p;
        if (yawDeg is { } y) payload["yawDeg"] = y;

        JsonNode? shot = await EditorIpcClient.TryPostAsync(payload, cancellationToken)
            ?? throw new InvalidOperationException(
                $"resident W3Editor not reachable at {EditorIpcClient.BaseUrl} — run editor_launch first");
        if (shot["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("screenshot failed: " + shot["error"]);
        return BuildResult(png, $"screenshot={png}\n{shot["statusLine"]}\ncamera={shot["camera"]?.ToJsonString()}");
    }

    private static CallToolResult BuildResult(string png, string text)
    {
        var content = new List<ContentBlock> { new TextContentBlock { Text = text.Trim() } };
        try
        {
            byte[] thumb = MakeJpegThumbnail(png, maxEdge: 1280, quality: 75);
            content.Add(new ImageContentBlock
            {
                MimeType = "image/jpeg",
                Data = thumb,
            });
        }
        catch (Exception ex)
        {
            content.Add(new TextContentBlock { Text = $"thumbnail skipped: {ex.Message}" });
        }
        return new CallToolResult { Content = content };
    }

    private static byte[] MakeJpegThumbnail(string pngPath, int maxEdge, long quality)
    {
        // Prefer SkiaSharp already referenced by W3Editor; fall back to full PNG if unavailable.
        using var input = File.OpenRead(pngPath);
        using var bitmap = SkiaSharp.SKBitmap.Decode(input)
            ?? throw new InvalidOperationException("Failed to decode PNG");
        int w = bitmap.Width, h = bitmap.Height;
        float scale = Math.Min(1f, maxEdge / (float)Math.Max(w, h));
        int tw = Math.Max(1, (int)(w * scale));
        int th = Math.Max(1, (int)(h * scale));
        SkiaSharp.SKBitmap? resized = bitmap.Resize(new SkiaSharp.SKImageInfo(tw, th), SkiaSharp.SKFilterQuality.Medium);
        try
        {
            using var image = SkiaSharp.SKImage.FromBitmap(resized ?? bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, (int)quality)
                ?? throw new InvalidOperationException("JPEG encode failed");
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose(); // bitmap itself is owned by the outer using
        }
    }
}
