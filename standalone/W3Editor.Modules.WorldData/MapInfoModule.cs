using W3Editor.Contracts;

namespace W3Editor.Modules.WorldData;

public sealed class MapInfoPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.mapinfo", "1.0.0", ContractVersion.Value,
        ModuleTier.Full, DisplayName: "地图信息 (war3map.w3i)");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new MapInfoCodec());
    public void Dispose() { }
}
