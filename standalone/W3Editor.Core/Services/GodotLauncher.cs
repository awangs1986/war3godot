using System.Diagnostics;
using System.Text;

namespace W3Editor.Services;

/// <summary>Locates the repo-local Godot executable and runs preview / headless import.</summary>
public static class GodotLauncher
{
    /// <summary>Walks up from the app folder to the repo root (contains project.godot).</summary>
    public static string? FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public static string? FindGodotExe(string projectRoot)
    {
        var exe = Path.Combine(projectRoot, ".tools", "godot-4.7.1-rc2-mono",
            "Godot_v4.7.1-rc2_mono_win64", "Godot_v4.7.1-rc2_mono_win64.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static (string exe, string root) Require()
    {
        var root = FindProjectRoot()
            ?? throw new InvalidOperationException("未找到 project.godot（工程根目录）");
        var exe = FindGodotExe(root)
            ?? throw new InvalidOperationException("未找到仓库内 Godot 4.7.1-rc2 mono 可执行文件（.tools/…）");
        return (exe, root);
    }

    /// <summary>
    /// Launches the sandbox preview. With a map path the sandbox loads it via
    /// the `--map=` user arg; otherwise it falls back to the default map.
    /// </summary>
    public static void LaunchPreview(string? mapPath = null)
    {
        var (exe, root) = Require();
        var args = new StringBuilder();
        args.Append("--path \"").Append(root).Append('"');
        args.Append(" res://scenes/w3_sandbox.tscn");
        if (!string.IsNullOrEmpty(mapPath))
            args.Append(" -- \"--map=").Append(mapPath).Append('"');
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args.ToString(),
            WorkingDirectory = root,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Starts a sandbox preview process configured for embedding (P2): windowed,
    /// fixed initial resolution, stdout untouched. Caller owns the Process.
    /// </summary>
    public static Process StartEmbeddedPreview(string? mapPath)
    {
        var (exe, root) = Require();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = root,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add("res://scenes/w3_sandbox.tscn");
        psi.ArgumentList.Add("--windowed");
        psi.ArgumentList.Add("--resolution");
        psi.ArgumentList.Add("1280x720");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--embedded");
        if (!string.IsNullOrEmpty(mapPath))
            psi.ArgumentList.Add("--map=" + mapPath);
        return Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Godot 预览进程");
    }

}
