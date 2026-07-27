using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using W3Editor.Contracts;

namespace W3Editor.Kernel;

public sealed class ModuleEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    /// <summary>Documentation only; the authority is the module's own manifest.</summary>
    [JsonPropertyName("tier")] public string Tier { get; set; } = "Full";
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class ModulesFile
{
    [JsonPropertyName("contractsVersion")] public string ContractsVersion { get; set; } = ContractVersion.Value;
    [JsonPropertyName("assemblies")] public List<string> Assemblies { get; set; } = new();
    [JsonPropertyName("modules")] public List<ModuleEntry> Modules { get; set; } = new();
}

public sealed record LoadedModule(IEditorModule Instance, ModuleManifest Manifest);

public sealed record ModuleLoadReport(
    IReadOnlyList<string> Loaded,
    IReadOnlyList<string> Disabled,
    IReadOnlyList<string> Unlisted,
    IReadOnlyList<(string Id, string Reason)> Skipped);

/// <summary>
/// Loads modules named in modules.json and owns the document lifecycle.
///
/// Discovery is manifest-driven on purpose: the old plugin loader recursively
/// scanned every DLL under a directory and loaded each into its own permanent
/// ALC (external review 4.24). Here, only the assemblies the manifest names are
/// touched, and a module absent from the manifest is simply never constructed —
/// that is the "delete one line to remove a feature" guarantee.
/// </summary>
public sealed class ModuleHost : IDisposable
{
    private readonly List<LoadedModule> _modules = new();
    private readonly Dictionary<string, IModuleContext> _contexts = new(StringComparer.Ordinal);
    private MapDocument? _document;

    public ModuleHost(EditorLog? log = null)
    {
        Log = log ?? new EditorLog();
        CapabilityRegistry = new CapabilityRegistry();
        Registry = new ModuleRegistry();
        History = new KernelEditHistory(Log);
        CapabilityRegistry.Provide<IEditHistory>(History, "kernel");
    }

    public EditorLog Log { get; }
    public CapabilityRegistry CapabilityRegistry { get; }
    public ICapabilities Capabilities => CapabilityRegistry;
    public ModuleRegistry Registry { get; }
    public KernelEditHistory History { get; }
    public IReadOnlyList<LoadedModule> Modules => _modules;

    public IMapDocument? Document => _document;
    public event Action<IMapDocument?>? DocumentChanged;

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    public static ModulesFile ReadManifest(string path)
    {
        var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<ModulesFile>(File.ReadAllBytes(path), options)
               ?? throw new InvalidDataException($"{path} is empty or not a module manifest.");
    }

    public ModuleLoadReport Load(string manifestPath, IReadOnlyList<Assembly>? extraAssemblies = null)
    {
        ModulesFile manifest = ReadManifest(manifestPath);
        if (!ContractVersion.IsCompatible(manifest.ContractsVersion))
            throw new InvalidDataException(
                $"modules.json declares contracts {manifest.ContractsVersion}, host provides {ContractVersion.Value}.");
        return Load(manifest, extraAssemblies);
    }

    public ModuleLoadReport Load(ModulesFile manifest, IReadOnlyList<Assembly>? extraAssemblies = null)
    {
        var wanted = manifest.Modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var loaded = new List<string>();
        var unlisted = new List<string>();
        var skipped = new List<(string, string)>();
        var disabled = manifest.Modules.Where(m => !m.Enabled).Select(m => m.Id).ToList();

        var candidates = new List<IEditorModule>();
        foreach (Assembly assembly in ResolveAssemblies(manifest, extraAssemblies, skipped))
        {
            foreach (Type type in SafeGetTypes(assembly, skipped))
            {
                if (!typeof(IEditorModule).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                    continue;
                try
                {
                    if (Activator.CreateInstance(type) is IEditorModule module)
                        candidates.Add(module);
                }
                catch (Exception ex)
                {
                    skipped.Add((type.FullName ?? type.Name, $"construction failed: {ex.Message}"));
                }
            }
        }

        var admitted = new List<IEditorModule>();
        foreach (IEditorModule module in candidates)
        {
            ModuleManifest m = module.Manifest;
            if (!wanted.TryGetValue(m.Id, out ModuleEntry? entry))
            {
                // Present in the binary but not in the manifest: intentionally inert.
                unlisted.Add(m.Id);
                module.Dispose();
                continue;
            }
            if (!entry.Enabled)
            {
                module.Dispose();
                continue;
            }
            if (!ContractVersion.IsCompatible(m.ContractsVersion))
            {
                skipped.Add((m.Id,
                    $"needs contracts {m.ContractsVersion}, host provides {ContractVersion.Value}"));
                module.Dispose();
                continue;
            }
            admitted.Add(module);
        }

        // Register in dependency order: a module whose required capabilities are
        // not yet provided waits for a later pass. Anything still unsatisfied
        // when no progress is possible is skipped with a precise reason.
        var pending = new List<IEditorModule>(admitted);
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (IEditorModule module in pending.ToList())
            {
                string[] missing = module.Manifest.Requires
                    .Where(name => !CapabilityRegistry.Has(name))
                    .ToArray();
                if (missing.Length > 0)
                    continue;

                var ctx = new ModuleContext(this, module.Manifest.Id);
                try
                {
                    module.Register(ctx);
                    _modules.Add(new LoadedModule(module, module.Manifest));
                    _contexts[module.Manifest.Id] = ctx;
                    loaded.Add(module.Manifest.Id);
                }
                catch (Exception ex)
                {
                    Log.Error($"Module '{module.Manifest.Id}' failed to register: {ex.Message}", ex);
                    skipped.Add((module.Manifest.Id, $"register threw: {ex.Message}"));
                    Registry.RemoveModule(module.Manifest.Id);
                    CapabilityRegistry.RemoveProvider(module.Manifest.Id);
                    TryDispose(module);
                }
                pending.Remove(module);
                progress = true;
            }
        }

        foreach (IEditorModule module in pending)
        {
            string[] missing = module.Manifest.Requires.Where(n => !CapabilityRegistry.Has(n)).ToArray();
            skipped.Add((module.Manifest.Id, "missing capabilities: " + string.Join(", ", missing)));
            TryDispose(module);
        }

        foreach (string id in wanted.Keys)
        {
            if (loaded.Contains(id) || disabled.Contains(id) || skipped.Any(s => s.Item1 == id))
                continue;
            skipped.Add((id, "listed in modules.json but no implementation found in the loaded assemblies"));
        }

        Log.Info($"Modules: {loaded.Count} loaded, {disabled.Count} disabled, {skipped.Count} skipped.");
        foreach ((string id, string reason) in skipped)
            Log.Warn($"Module '{id}' skipped: {reason}");

        return new ModuleLoadReport(loaded, disabled, unlisted, skipped);
    }

    private IEnumerable<Assembly> ResolveAssemblies(
        ModulesFile manifest, IReadOnlyList<Assembly>? extra, List<(string, string)> skipped)
    {
        var seen = new HashSet<Assembly>();
        foreach (Assembly a in extra ?? Array.Empty<Assembly>())
            if (seen.Add(a))
                yield return a;

        string baseDir = AppContext.BaseDirectory;
        foreach (string name in manifest.Assemblies)
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                string path = Path.Combine(baseDir, name + ".dll");
                if (!File.Exists(path))
                {
                    skipped.Add((name, $"assembly not found: {path}"));
                    continue;
                }
                try
                {
                    assembly = Assembly.LoadFrom(path);
                }
                catch (Exception ex)
                {
                    skipped.Add((name, $"assembly load failed: {ex.Message}"));
                    continue;
                }
            }
            if (seen.Add(assembly))
                yield return assembly;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly, List<(string, string)> skipped)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            skipped.Add((assembly.GetName().Name ?? "?",
                $"partial type load: {ex.LoaderExceptions.FirstOrDefault()?.Message}"));
            return ex.Types.Where(t => t != null)!;
        }
    }

    private static void TryDispose(IEditorModule module)
    {
        try { module.Dispose(); }
        catch { /* a module that throws on dispose must not abort loading */ }
    }

    // -----------------------------------------------------------------
    // Document lifecycle
    // -----------------------------------------------------------------

    public MapDocument OpenDocument(string folder, MapFormatKind format)
    {
        folder = Path.GetFullPath(folder);
        AtomicFileSet.SweepStaleTemps(folder);
        var document = new MapDocument(folder, format, Log, History);
        document.Load(Registry.Codecs);
        History.Clear();
        _document = document;
        DocumentChanged?.Invoke(document);
        Log.Info($"Opened {folder} ({format}): " +
                 $"{document.Parts.Count} parts, {document.UnknownParts.Count} passthrough files.");
        return document;
    }

    public void CloseDocument()
    {
        _document = null;
        History.Clear();
        DocumentChanged?.Invoke(null);
    }

    public void Dispose()
    {
        foreach (LoadedModule module in _modules)
            TryDispose(module.Instance);
        _modules.Clear();
        _contexts.Clear();
    }
}
