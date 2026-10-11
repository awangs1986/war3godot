using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HiveWE.Mcp;

/// <summary>
/// Controls a local HiveWE.exe process and indexes its source tree for algorithm cross-checks.
/// HiveWE already accepts <c>--map &lt;folder&gt;</c> (see D:\temp\HiveWE\src\main.cpp).
/// Edit commands go through localhost JSON IPC (default http://127.0.0.1:17321/).
/// </summary>
public sealed class HiveWeSession
{
    // Machine defaults, overridable per environment (portability).
    public string ExePath { get; set; } =
        Environment.GetEnvironmentVariable("HIVEWE_EXE") is { Length: > 0 } exe
            ? exe
            : @"D:\temp\HiveWE\build\Release\Release\HiveWE.exe";
    public string SourceRoot { get; set; } =
        Environment.GetEnvironmentVariable("HIVEWE_ROOT") is { Length: > 0 } root
            ? root
            : @"D:\temp\HiveWE";
    public string? LastMapPath { get; private set; }
    public Process? Process { get; private set; }
    public int IpcPort { get; set; } = 17321;

    public string WorkingDirectory => Path.GetDirectoryName(ExePath) ?? ".";
    public string IpcBaseUrl => $"http://127.0.0.1:{IpcPort}/";

    /// <summary>Screenshot/dump output dir: HIVEWE_OUT_DIR env or %TEMP%\hivewe.</summary>
    public static string OutDir =>
        Environment.GetEnvironmentVariable("HIVEWE_OUT_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Path.GetTempPath(), "hivewe");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
    };

    public string StatusJson()
    {
        bool running = Process is { HasExited: false };
        string? ipc = null;
        try
        {
            if (running)
                ipc = IpcPingAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ipc = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
        return JsonSerializer.Serialize(new
        {
            exe = ExePath,
            exeExists = File.Exists(ExePath),
            sourceRoot = SourceRoot,
            sourceExists = Directory.Exists(SourceRoot),
            workingDirectory = WorkingDirectory,
            running,
            pid = running ? Process!.Id : (int?)null,
            lastMap = LastMapPath,
            ipcPort = IpcPort,
            ipcBaseUrl = IpcBaseUrl,
            ipc,
            supports = new[] { "--map <folder-or-path>", "--ipc-port <n>", "--no-ipc", "POST / JSON cmd" },
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Configure(string? exePath, string? sourceRoot)
    {
        if (!string.IsNullOrWhiteSpace(exePath))
            ExePath = Path.GetFullPath(exePath);
        if (!string.IsNullOrWhiteSpace(sourceRoot))
            SourceRoot = Path.GetFullPath(sourceRoot);
    }

    /// <summary>Launch HiveWE, optionally with --map. Replaces any previous instance we started.</summary>
    public string Launch(string? mapPath, bool restart = true)
    {
        if (!File.Exists(ExePath))
            throw new FileNotFoundException("HiveWE.exe not found", ExePath);
        if (!Directory.Exists(WorkingDirectory))
            throw new DirectoryNotFoundException(WorkingDirectory);

        if (!restart && Process is { HasExited: false })
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                alreadyRunning = true,
                pid = Process.Id,
                map = LastMapPath,
            });
        }

        if (restart)
            Close(force: true);

        string? map = null;
        if (!string.IsNullOrWhiteSpace(mapPath))
        {
            map = Path.GetFullPath(mapPath);
            if (!Directory.Exists(map) && !File.Exists(map))
                throw new FileNotFoundException("Map path not found", map);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--ipc-port");
        psi.ArgumentList.Add(IpcPort.ToString());
        if (map != null)
        {
            psi.ArgumentList.Add("--map");
            psi.ArgumentList.Add(map);
        }

        Process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start HiveWE.exe");
        LastMapPath = map;
        // Wait until IPC answers (CASC/map load can take a while; port binds before map load).
        try
        {
            WaitForIpcAsync(TimeSpan.FromSeconds(30), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Still return launch ok; caller can retry Ipc.
        }
        return JsonSerializer.Serialize(new
        {
            ok = true,
            pid = Process.Id,
            map,
            cwd = WorkingDirectory,
            ipcPort = IpcPort,
            ipcBaseUrl = IpcBaseUrl,
        });
    }

    public async Task WaitForIpcAsync(TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Exception? last = null;
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await IpcPingAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(200, ct);
            }
        }
        throw new TimeoutException($"HiveWE IPC not ready at {IpcBaseUrl}: {last?.Message}");
    }

    public Task<string> IpcPingAsync(CancellationToken ct) =>
        IpcAsync(new { cmd = "ping" }, ct);

    public Task<string> IpcStatusAsync(CancellationToken ct) =>
        IpcAsync(new { cmd = "status" }, ct);

    public Task<string> IpcOpenMapAsync(string mapPath, CancellationToken ct) =>
        IpcAsync(new { cmd = "open_map", path = Path.GetFullPath(mapPath) }, ct);

    public Task<string> IpcSaveAsync(string? path, CancellationToken ct) =>
        path == null
            ? IpcAsync(new { cmd = "save" }, ct)
            : IpcAsync(new { cmd = "save", path = Path.GetFullPath(path) }, ct);

    public Task<string> IpcBrushStampAsync(
        string op, float cx, float cy, int size, double strength, string? tileId, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "brush_stamp",
            ["op"] = op,
            ["cx"] = cx,
            ["cy"] = cy,
            ["size"] = size,
            ["strength"] = strength,
        };
        if (!string.IsNullOrWhiteSpace(tileId))
            payload["tile_id"] = tileId;
        return IpcAsync(payload, ct);
    }

    public Task<string> IpcPlaceDoodadAsync(string id, float x, float y, int variation, CancellationToken ct) =>
        IpcAsync(new { cmd = "place_doodad", id, x, y, variation }, ct);

    public Task<string> IpcPlaceUnitAsync(string id, float x, float y, CancellationToken ct) =>
        IpcAsync(new { cmd = "place_unit", id, x, y }, ct);

    public Task<string> IpcUndoAsync(CancellationToken ct) => IpcAsync(new { cmd = "undo" }, ct);
    public Task<string> IpcRedoAsync(CancellationToken ct) => IpcAsync(new { cmd = "redo" }, ct);

    public async Task<string> IpcAsync(object payload, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(IpcBaseUrl, content, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"IPC HTTP {(int)resp.StatusCode}: {body}");
        return body;
    }

    /// <summary>Ensure process is up, then wait for IPC.</summary>
    public async Task EnsureIpcReadyAsync(CancellationToken ct)
    {
        if (Process is null or { HasExited: true })
            Launch(LastMapPath, restart: false);
        await WaitForIpcAsync(TimeSpan.FromSeconds(45), ct);
    }

    public string Close(bool force = false)
    {
        if (Process == null || Process.HasExited)
        {
            Process = null;
            // Also try to close stray HiveWE we didn't start.
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("HiveWE"))
            {
                try
                {
                    if (force) p.Kill(entireProcessTree: true);
                    else p.CloseMainWindow();
                }
                catch { /* ignore */ }
            }
            return JsonSerializer.Serialize(new { ok = true, closed = "none-or-stray" });
        }
        try
        {
            if (!Process.CloseMainWindow() || force)
                Process.Kill(entireProcessTree: true);
            Process.WaitForExit(5000);
        }
        catch { /* ignore */ }
        int? code = Process.HasExited ? Process.ExitCode : null;
        Process.Dispose();
        Process = null;
        return JsonSerializer.Serialize(new { ok = true, exitCode = code });
    }

    public async Task<string> ScreenshotAsync(string? outputPath, int waitMs, CancellationToken ct)
    {
        IntPtr hwnd = await WaitForMainWindowAsync(Math.Max(waitMs, 500), ct);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("HiveWE main window not found. Is it running / still loading CASC?");

        string outDir = OutDir;
        Directory.CreateDirectory(outDir);
        string png = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(outDir, $"hivewe_{DateTime.Now:yyyyMMdd_HHmmss}.png")
            : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);

        if (!NativeCapture.CaptureWindow(hwnd, png))
            throw new InvalidOperationException("PrintWindow failed");
        return png;
    }

    public async Task<IntPtr> WaitForMainWindowAsync(int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            IntPtr hwnd = FindHiveWeWindow();
            if (hwnd != IntPtr.Zero)
                return hwnd;
            await Task.Delay(200, ct);
        }
        return FindHiveWeWindow();
    }

    public IntPtr FindHiveWeWindow()
    {
        if (Process is { HasExited: false } p)
        {
            p.Refresh();
            if (p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        }
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("HiveWE"))
        {
            try
            {
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;
            }
            catch { /* ignore */ }
        }
        return IntPtr.Zero;
    }

    /// <summary>Search HiveWE source (cpp/h/ixx/cppm) for a regex; returns path:line snippets.</summary>
    public string SourceSearch(string pattern, string? glob, int maxHits)
    {
        if (!Directory.Exists(SourceRoot))
            throw new DirectoryNotFoundException(SourceRoot);
        maxHits = Math.Clamp(maxHits, 1, 100);
        var rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        string[] exts = string.IsNullOrWhiteSpace(glob)
            ? new[] { "*.cpp", "*.h", "*.hpp", "*.ixx", "*.cppm", "*.md", "*.cmake" }
            : new[] { glob! };

        var hits = new List<object>();
        foreach (string ext in exts)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(SourceRoot, ext, SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }
            foreach (string file in files)
            {
                // Skip build trees / vcpkg noise.
                if (file.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!rx.IsMatch(lines[i])) continue;
                    string rel = Path.GetRelativePath(SourceRoot, file);
                    hits.Add(new
                    {
                        path = rel,
                        absolute = file,
                        line = i + 1,
                        text = lines[i].Trim(),
                    });
                    if (hits.Count >= maxHits)
                        return JsonSerializer.Serialize(new { count = hits.Count, truncated = true, hits },
                            new JsonSerializerOptions { WriteIndented = true });
                }
            }
        }
        return JsonSerializer.Serialize(new { count = hits.Count, truncated = false, hits },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public string SourceRead(string relativeOrAbsolute, int startLine, int lineCount)
    {
        string path = Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.Combine(SourceRoot, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar));
        path = Path.GetFullPath(path);
        string rootFull = Path.GetFullPath(SourceRoot);
        if (!path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path must be under HiveWE source root.");
        if (!File.Exists(path))
            throw new FileNotFoundException(path);

        startLine = Math.Max(1, startLine);
        lineCount = Math.Clamp(lineCount, 1, 400);
        var lines = File.ReadAllLines(path);
        int from = startLine - 1;
        if (from >= lines.Length)
            return JsonSerializer.Serialize(new { path, startLine, lines = Array.Empty<string>() });
        var slice = lines.Skip(from).Take(lineCount)
            .Select((t, i) => $"{startLine + i}|{t}")
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            path = Path.GetRelativePath(SourceRoot, path),
            absolute = path,
            startLine,
            endLine = startLine + slice.Length - 1,
            totalLines = lines.Length,
            lines = slice,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ListSourceTree(string? relative, int depth, int maxEntries)
    {
        string dir = string.IsNullOrWhiteSpace(relative)
            ? SourceRoot
            : Path.Combine(SourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        dir = Path.GetFullPath(dir);
        if (!dir.StartsWith(Path.GetFullPath(SourceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path must be under source root.");
        depth = Math.Clamp(depth, 1, 4);
        maxEntries = Math.Clamp(maxEntries, 1, 200);
        var entries = new List<object>();
        void Walk(string d, int dpt)
        {
            if (entries.Count >= maxEntries) return;
            foreach (string sub in Directory.EnumerateDirectories(d).OrderBy(x => x))
            {
                if (entries.Count >= maxEntries) return;
                string name = Path.GetFileName(sub);
                if (name is "build" or ".git" or "vcpkg_installed") continue;
                entries.Add(new { type = "dir", path = Path.GetRelativePath(SourceRoot, sub) });
                if (dpt + 1 < depth) Walk(sub, dpt + 1);
            }
            foreach (string file in Directory.EnumerateFiles(d).OrderBy(x => x))
            {
                if (entries.Count >= maxEntries) return;
                entries.Add(new
                {
                    type = "file",
                    path = Path.GetRelativePath(SourceRoot, file),
                    bytes = new FileInfo(file).Length,
                });
            }
        }
        Walk(dir, 0);
        return JsonSerializer.Serialize(new { root = Path.GetRelativePath(SourceRoot, dir), count = entries.Count, entries },
            new JsonSerializerOptions { WriteIndented = true });
    }
}

internal static class NativeCapture
{
    public static bool CaptureWindow(IntPtr hwnd, string pngPath)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc))
            return false;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return false;
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) return false;
        }
        bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
        return true;
    }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
