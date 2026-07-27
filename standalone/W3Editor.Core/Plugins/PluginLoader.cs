using System.Reflection;
using System.Runtime.Loader;
using W3Editor.PluginContracts;

namespace W3Editor.Plugins;

/// <summary>
/// Plugins run with FULL TRUST in-process: the per-DLL AssemblyLoadContext below
/// exists for dependency resolution only and is NOT a security sandbox (this is
/// deliberate — see docs/superpowers/specs/2026-07-23-fbx-import-plugin-design.md).
/// </summary>
public static class PluginLoader
{
    private static readonly AssemblyName ContractsAssemblyName =
        typeof(IW3EditorPlugin).Assembly.GetName();

    public static IReadOnlyList<IW3EditorPlugin> LoadAll(string pluginsDir)
        => LoadAll(pluginsDir, out _);

    /// <summary>
    /// Loads plugins; DLLs that fail to load are reported via
    /// <paramref name="loadFailures"/> ("name: reason") so the UI can surface
    /// them instead of the plugin silently vanishing.
    /// </summary>
    public static IReadOnlyList<IW3EditorPlugin> LoadAll(
        string pluginsDir, out IReadOnlyList<string> loadFailures)
    {
        var failures = new List<string>();
        loadFailures = failures;
        if (!Directory.Exists(pluginsDir))
            return Array.Empty<IW3EditorPlugin>();

        var plugins = new List<IW3EditorPlugin>();
        foreach (string dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(dllPath);
            if (fileName.Contains("W3Editor.PluginContracts", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("Assimp", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var context = new PluginAssemblyLoadContext(dllPath);
                Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
                foreach (Type type in assembly.GetExportedTypes())
                {
                    if (!typeof(IW3EditorPlugin).IsAssignableFrom(type)
                        || type.IsAbstract
                        || type.IsInterface)
                        continue;

                    if (Activator.CreateInstance(type) is IW3EditorPlugin plugin)
                        plugins.Add(plugin);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PluginLoader: skip {dllPath}: {ex.Message}");
                failures.Add($"{fileName}: {ex.Message}");
            }
        }

        return plugins;
    }

    private sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == ContractsAssemblyName.Name)
                return AssemblyLoadContext.Default.LoadFromAssemblyName(ContractsAssemblyName);

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}
