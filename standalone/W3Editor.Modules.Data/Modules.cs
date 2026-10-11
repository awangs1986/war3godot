using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

public abstract class DataModule(string id, string name, ModuleTier tier = ModuleTier.Full) : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(id,"1.0.0",ContractVersion.Value,tier,DisplayName:name);
    public abstract void Register(IModuleContext ctx);
    public void Dispose() { }
    protected static void WindowCommand(IModuleContext ctx,string commandId,string name,string windowId,string menu,int order=200)
        => ctx.AddCommand(new CommandDescriptor(commandId,name,(runtime,token)=>runtime.Capabilities.Get<IWindowService>()!.OpenAsync(windowId,token),menu,Order:order,
            CanExecute:runtime=>runtime.Document!=null&&runtime.Capabilities.Get<IWindowService>()?.CanOpen(windowId)==true));
}

public sealed class SoundsPartModule : DataModule
{
    public SoundsPartModule():base("part.sounds","声音数据"){}
    public override void Register(IModuleContext ctx){ctx.AddPartCodec(new SoundsCodec());ctx.AddWindow(new SimpleWindowDescriptor("window.sounds","声音编辑器",c=>new SoundsWindow(c)));WindowCommand(ctx,"part.sounds","声音编辑器","window.sounds","窗口",210);}
}
public sealed class ImportsPartModule : DataModule
{
    public ImportsPartModule():base("part.imports","导入表"){}
    public override void Register(IModuleContext ctx){ctx.AddPartCodec(new ImportsCodec());ctx.AddWindow(new SimpleWindowDescriptor("window.imports","导入管理器",c=>new ImportsWindow(c)));WindowCommand(ctx,"part.imports","导入管理器","window.imports","窗口",220);}
}
public sealed class GameplayPartModule : DataModule
{
    public GameplayPartModule():base("part.gameplay","玩法常量数据"){}
    public override void Register(IModuleContext ctx)=>ctx.AddPartCodec(new TextFilesCodec("gameplay",42,"war3mapMisc.txt","war3mapSkin.txt"));
}
public sealed class TriggerStringsPartModule : DataModule
{
    public TriggerStringsPartModule():base("part.triggerstrings","触发器字符串"){}
    public override void Register(IModuleContext ctx)=>ctx.AddPartCodec(new TextFilesCodec("triggerstrings",43,"war3map.wts"));
}
public sealed class TriggersPartModule : DataModule
{
    public TriggersPartModule():base("part.triggers","触发器与脚本数据"){}
    public override void Register(IModuleContext ctx)=>ctx.AddPartCodec(new TriggerWorkspaceCodec());
}
public sealed class GameplayDialogModule : DataModule
{
    public GameplayDialogModule():base("dialog.gameplay","玩法常量"){}
    public override void Register(IModuleContext ctx){ctx.AddWindow(new SimpleWindowDescriptor("window.gameplay","玩法常量",c=>new TextPartWindow(c,"gameplay",new[]{"war3mapMisc.txt","war3mapSkin.txt"})));WindowCommand(ctx,"dialog.gameplay","玩法常量","window.gameplay","地图",230);}
}
public sealed class TriggerEditorModule : DataModule
{
    public TriggerEditorModule():base("editor.trigger","触发器编辑器"){}
    public override void Register(IModuleContext ctx){ctx.AddWindow(new SimpleWindowDescriptor("window.trigger-editor","触发器 / 脚本编辑器",c=>new TriggerEditorWindow(c)));WindowCommand(ctx,"editor.trigger","触发器编辑器","window.trigger-editor","窗口",240);}
}
public sealed class ModelEditorModule : DataModule
{
    public ModelEditorModule():base("editor.model","模型检查器"){}
    public override void Register(IModuleContext ctx){ctx.AddWindow(new SimpleWindowDescriptor("window.model-editor","MDX / MDL 模型检查器",c=>new ModelInspectorWindow(c)));WindowCommand(ctx,"editor.model","模型检查器","window.model-editor","窗口",250);}
}
public sealed class ProtectionDialogModule : DataModule
{
    public ProtectionDialogModule():base("dialog.protection","地图保护设置"){}
    public override void Register(IModuleContext ctx){ctx.AddWindow(new SimpleWindowDescriptor("window.protection","地图保护",c=>new ProtectionWindow(c)));WindowCommand(ctx,"dialog.protection","地图保护设置","window.protection","地图",260);}
}
public sealed class ProtectionOperationModule : DataModule
{
    public ProtectionOperationModule():base("op.protection","导出受保护地图"){}
    public override void Register(IModuleContext ctx)=>WindowCommand(ctx,"op.protection","导出受保护地图","window.protection","文件",270);
}
public sealed class SwitchWarcraftOperationModule : DataModule
{
    public SwitchWarcraftOperationModule():base("op.switchwarcraft","切换 Warcraft 安装"){}
    public override void Register(IModuleContext ctx){ctx.AddWindow(new SimpleWindowDescriptor("window.switch-warcraft","切换 Warcraft 安装",c=>new SwitchWarcraftWindow()));WindowCommand(ctx,"op.switchwarcraft","切换 Warcraft 安装","window.switch-warcraft","工具",280);}
}

internal sealed class SimpleWindowDescriptor(string id,string title,Func<IModuleContext,object> factory):IWindowDescriptor
{public string Id=>id;public string Title=>title;public int DefaultWidth=>1000;public int DefaultHeight=>700;public object CreateWindow(IModuleContext ctx)=>factory(ctx);}
