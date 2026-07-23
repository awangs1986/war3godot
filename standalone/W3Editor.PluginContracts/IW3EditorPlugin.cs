namespace W3Editor.PluginContracts;

public interface IW3EditorPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    void Register(IEditorPluginContext ctx);
}
