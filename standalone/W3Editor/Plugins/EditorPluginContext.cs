using Avalonia.Platform.Storage;
using W3Editor.Dialogs;
using W3Editor.PluginContracts;
using W3Editor.Services;

namespace W3Editor.Plugins;

/// <summary>
/// Host-side <see cref="IEditorPluginContext"/> bound to the current
/// <see cref="MainWindow"/> / <see cref="MapSession"/>.
/// </summary>
public sealed class EditorPluginContext : IEditorPluginContext
{
    private readonly MainWindow _window;

    public EditorPluginContext(MainWindow window)
    {
        _window = window;
    }

    public bool HasOpenMap => _window.Session.IsOpen;

    public string? CurrentMapFolder =>
        _window.Session.IsOpen && _window.Session.IsFolder
            ? _window.Session.SourcePath
            : null;

    public string SharedModelsRoot => EditorPaths.SharedModelsRoot;

    public void AddMenuCommand(string menuPath, string id, Func<CancellationToken, Task> handler)
        => _window.AddPluginMenuCommand(menuPath, id, handler);

    public async Task<string?> PickOpenFileAsync(string title, params string[] filters)
    {
        var types = new List<FilePickerFileType>();
        if (filters is { Length: > 0 })
        {
            foreach (string filter in filters)
            {
                string[] patterns = filter.Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (patterns.Length == 0)
                    continue;
                types.Add(new FilePickerFileType(filter) { Patterns = patterns });
            }
        }

        types.Add(new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } });

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
        });
        if (files.Count == 0)
            return null;
        return files[0].TryGetLocalPath();
    }

    public void PlacePreviewMesh(PluginMeshPreview mesh, string label)
        => _window.PlacePluginPreview(mesh, label);

    public void ShowReport(string title, PluginValidationReport report)
    {
        PluginReportDialog.Show(_window, title, report).ContinueWith(
            t => Console.Error.WriteLine($"ShowReport: {t.Exception!.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
