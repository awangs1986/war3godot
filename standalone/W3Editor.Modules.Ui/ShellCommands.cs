using W3Editor.Contracts;

namespace W3Editor.Modules.Ui;

public abstract class ShellCommandModule : IEditorModule
{
    private readonly string _menuPath;
    private readonly string? _shortcut;
    private readonly int _order;

    protected ShellCommandModule(
        string id,
        string displayName,
        string menuPath,
        int order,
        string? shortcut = null)
    {
        Manifest = new ModuleManifest(
            id,
            "1.0.0",
            ContractVersion.Value,
            ModuleTier.Full,
            DisplayName: displayName);
        _menuPath = menuPath;
        _shortcut = shortcut;
        _order = order;
    }

    public ModuleManifest Manifest { get; }

    public void Register(IModuleContext ctx) => ctx.AddCommand(new CommandDescriptor(
        Manifest.Id,
        Manifest.DisplayName,
        async (context, cancellationToken) =>
        {
            IEditorShellActions actions = context.Capabilities.Get<IEditorShellActions>()
                ?? throw new InvalidOperationException("The editor Shell action service is unavailable.");
            await actions.ExecuteAsync(Manifest.Id, cancellationToken);
        },
        _menuPath,
        _shortcut,
        _order,
        context => context.Capabilities.Get<IEditorShellActions>()?.CanExecute(Manifest.Id) == true));

    public void Dispose() { }
}

public sealed class NewMapOperationModule : ShellCommandModule
{
    public NewMapOperationModule() : base("op.newmap", "新建地图…", "文件", 10, "Ctrl+N") { }
}

public sealed class OpenMapOperationModule : ShellCommandModule
{
    public OpenMapOperationModule() : base("op.open", "打开地图…", "文件", 20, "Ctrl+O") { }
}

public sealed class SaveOperationModule : ShellCommandModule
{
    public SaveOperationModule() : base("op.save", "保存地图", "文件", 30, "Ctrl+S") { }
}

public sealed class SaveAsOperationModule : ShellCommandModule
{
    public SaveAsOperationModule() : base("op.saveas", "另存地图…", "文件", 40) { }
}

public sealed class ExportOperationModule : ShellCommandModule
{
    public ExportOperationModule() : base("op.export", "导出 d1map…", "文件", 50) { }
}

public sealed class ChangeTilesetOperationModule : ShellCommandModule
{
    public ChangeTilesetOperationModule() : base("op.changetileset", "更换图块集…", "编辑", 60) { }
}

public sealed class ChangeTilePathingOperationModule : ShellCommandModule
{
    public ChangeTilePathingOperationModule() : base("op.changetilepathing", "全局图块寻路…", "编辑", 70) { }
}

public sealed class TileSetterDialogModule : ShellCommandModule
{
    public TileSetterDialogModule() : base("dialog.tilesetter", "图块集设置器", "窗口", 110) { }
}

public sealed class TilePickerDialogModule : ShellCommandModule
{
    public TilePickerDialogModule() : base("dialog.tilepicker", "图块选择器", "窗口", 120) { }
}

public sealed class TilePatherDialogModule : ShellCommandModule
{
    public TilePatherDialogModule() : base("dialog.tilepather", "图块寻路设置器", "窗口", 130) { }
}

public sealed class CameraViewModule : ShellCommandModule
{
    public CameraViewModule() : base("view.camera", "切换 3D 摄像机视图", "视图", 210) { }
}

public sealed class TerrainRenderModule : ShellCommandModule
{
    public TerrainRenderModule() : base("render.terrain", "地形渲染", "视图/渲染", 220) { }
}

public sealed class WaterRenderModule : ShellCommandModule
{
    public WaterRenderModule() : base("render.water", "水面渲染", "视图/渲染", 230) { }
}

public sealed class CliffRenderModule : ShellCommandModule
{
    public CliffRenderModule() : base("render.cliff", "悬崖渲染", "视图/渲染", 240) { }
}

public sealed class MapInfoDialogModule : ShellCommandModule
{
    public MapInfoDialogModule() : base("dialog.mapinfo", "地图信息…", "地图", 140) { }
}

public sealed class ImportHeightmapOperationModule : ShellCommandModule
{
    public ImportHeightmapOperationModule() : base("op.importheightmap", "导入高度图…", "地形", 80) { }
}

public sealed class TilesetsPartModule : ShellCommandModule
{
    public TilesetsPartModule() : base("part.tilesets", "图块集数据", "地图", 145) { }
}

public sealed class ResizeOperationModule : ShellCommandModule
{
    public ResizeOperationModule() : base("op.resize", "调整地图大小…", "地图", 150) { }
}

public sealed class UnusedFilesOperationModule : ShellCommandModule
{
    public UnusedFilesOperationModule() : base("op.unusedfiles", "未使用文件报告", "工具", 160) { }
}

public sealed class CopyPasteOperationModule : ShellCommandModule
{
    public CopyPasteOperationModule() : base("op.copypaste", "复制/粘贴摆放", "编辑", 170) { }
}

public sealed class ViewHistoryOperationModule : ShellCommandModule
{
    public ViewHistoryOperationModule() : base("op.viewhistory", "查看撤销历史", "编辑", 180) { }
}

public sealed class TestMapOperationModule : ShellCommandModule
{
    public TestMapOperationModule() : base("op.testmap", "测试地图", "文件", 190) { }
}

public sealed class VisibilityViewModule : ShellCommandModule
{
    public VisibilityViewModule() : base("view.visibility", "可见性设置", "视图", 200) { }
}

public sealed class SettingsDialogModule : ShellCommandModule
{
    public SettingsDialogModule() : base("dialog.settings", "编辑器设置…", "工具", 250) { }
}

public sealed class AssetManagerModule : ShellCommandModule
{
    public AssetManagerModule() : base("manager.asset", "资源管理器", "窗口", 260) { }
}

public sealed class D1MapFormatModule : ShellCommandModule
{
    public D1MapFormatModule() : base("format.d1map", "d1map 格式信息", "工具", 300) { }
}

public sealed class GodotBridgeModule : ShellCommandModule
{
    public GodotBridgeModule() : base("bridge.godot", "在 Godot 中预览", "文件", 310) { }
}

public sealed class FbxImportModule : ShellCommandModule
{
    public FbxImportModule() : base("import.fbx", "FBX 导入器", "工具/导入", 320) { }
}

public sealed class PluginHostModule : ShellCommandModule
{
    public PluginHostModule() : base("host.plugin", "插件宿主", "工具/插件", 330) { }
}

public sealed class AssetPackModule : ShellCommandModule
{
    public AssetPackModule() : base("assets.pack", "资产包状态", "工具/资源", 340) { }
}

public sealed class PbrRenderModule : ShellCommandModule
{
    public PbrRenderModule() : base("render.pbr", "PBR 渲染预览", "视图/渲染", 350) { }
}
