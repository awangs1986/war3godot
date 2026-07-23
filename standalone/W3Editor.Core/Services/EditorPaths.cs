namespace W3Editor.Services;

/// <summary>
/// Machine-independent output locations for screenshots, probe dumps and
/// acceptance artifacts. Override with the W3EDITOR_OUT_DIR environment
/// variable; defaults to %TEMP%\w3editor.
/// </summary>
public static class EditorPaths
{
    public static string OutDir =>
        Environment.GetEnvironmentVariable("W3EDITOR_OUT_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Path.GetTempPath(), "w3editor");

    /// <summary>Path under <see cref="OutDir"/>; creates the directory.</summary>
    public static string OutFile(string fileName)
    {
        string dir = OutDir;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    /// <summary>
    /// Directory scanned for plugin DLLs. Override with W3EDITOR_PLUGINS_DIR;
    /// defaults to {AppContext.BaseDirectory}/plugins.
    /// </summary>
    public static string PluginsDir =>
        Environment.GetEnvironmentVariable("W3EDITOR_PLUGINS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Shared model import root for plugins. Override with W3EDITOR_SHARED_MODELS;
    /// defaults to {OutDir}/shared_models.
    /// </summary>
    public static string SharedModelsRoot =>
        Environment.GetEnvironmentVariable("W3EDITOR_SHARED_MODELS") is { Length: > 0 } dir
            ? dir
            : Path.Combine(OutDir, "shared_models");
}
