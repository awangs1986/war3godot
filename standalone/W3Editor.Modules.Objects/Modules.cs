using W3Editor.Contracts;

namespace W3Editor.Modules.Objects;

public sealed class ObjectsPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.objects", "1.0.0", ContractVersion.Value, ModuleTier.Full,
        DisplayName: "Warcraft 对象修改表");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new ObjectDataCodec());
    public void Dispose() { }
}

public sealed class ObjectEditorModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "editor.object", "1.0.0", ContractVersion.Value, ModuleTier.Full,
        RequiresCapabilities: new[] { typeof(IWindowService).FullName! },
        DisplayName: "对象编辑器");

    public void Register(IModuleContext ctx)
    {
        ctx.AddWindow(new ObjectEditorWindowDescriptor());
        ctx.AddCommand(new CommandDescriptor(
            "editor.object", "对象编辑器",
            (runtime, token) => runtime.Capabilities.Get<IWindowService>()!
                .OpenAsync("window.object-editor", token),
            "窗口/对象编辑器", "F6", 100,
            runtime => runtime.Document?.GetPart<ObjectDataPart>() != null
                       && runtime.Capabilities.Get<IWindowService>()?.CanOpen("window.object-editor") == true));
    }

    public void Dispose() { }
}

internal sealed class ObjectEditorWindowDescriptor : IWindowDescriptor
{
    public string Id => "window.object-editor";
    public string Title => "W3 对象编辑器";
    public int DefaultWidth => 1180;
    public int DefaultHeight => 760;
    public object CreateWindow(IModuleContext ctx) => new ObjectEditorWindow(ctx);
}
