namespace W3Editor.PluginContracts;

public interface IEditorPluginContext
{
    void AddMenuCommand(string menuPath, string id, Func<CancellationToken, Task> handler);
    Task<string?> PickOpenFileAsync(string title, params string[] filters);
    bool HasOpenMap { get; }
    string? CurrentMapFolder { get; }
    string SharedModelsRoot { get; }
    void PlacePreviewMesh(PluginMeshPreview mesh, string label);
    void ShowReport(string title, PluginValidationReport report);
}
