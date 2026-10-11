using Diablo1Like.W3.Level;
using Diablo1Like.W3.World;
using Godot;

namespace Diablo1Like.Tests;

/// <summary>
/// 验证导入后的 LevelData 可直接驱动运行时地形（无需再解析 W3E）。
/// 用法：godot --headless --path . res://tools/w3_level_runtime_smoke.tscn [-- sample_dir]
/// </summary>
public partial class W3LevelRuntimeSmoke : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private void Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string sampleDir = ProjectSettings.GlobalizePath("res://testdata/hivewe_sample");
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) && !args[0].StartsWith("--"))
            sampleDir = args[0];

        const string output = "user://w3_runtime_smoke.res";
        int importError = W3LevelImporterBridge.ImportToFile(sampleDir, output);
        if (importError != (int)Error.Ok)
        {
            GD.PushError($"RUNTIME_IMPORT_FAILED: {W3LevelImporterBridge.GetLastError()}");
            GetTree().Quit(1);
            return;
        }

        var level = ResourceLoader.Load(output, "", ResourceLoader.CacheMode.Ignore) as W3LevelDataResource;
        var world = new W3MapWorld { terrain_only = true };
        GetTree().Root.AddChild(world);
        bool ok = world.load_path(output);
        string? mapSha = null;
        if (world.map != null)
            mapSha = world.map.Get("source_w3e_sha256").AsString();

        if (!ok
            || world.map == null
            || world.sampler == null
            || level == null
            || mapSha != level.source_w3e_sha256)
        {
            GD.PushError($"RUNTIME_LEVEL_FAILED: {world.get_status()}");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"RUNTIME_LEVEL_OK: {level.TileCountX()}x{level.TileCountY()} sha256={level.source_w3e_sha256}");
        GetTree().Quit(0);
    }
}
