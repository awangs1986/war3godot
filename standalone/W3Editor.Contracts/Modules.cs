namespace W3Editor.Contracts;

/// <summary>
/// Implementation depth of a module. The 1:1 HiveWE coverage claim is about
/// the contract surface, not about every module being production-grade.
/// </summary>
public enum ModuleTier
{
    /// <summary>Fully implemented and maintained.</summary>
    Full,
    /// <summary>
    /// Skeleton: registers its part codec so the data round-trips byte-exactly,
    /// but offers no editing UI. Removing it must not lose the data — the part
    /// falls back to <see cref="UnknownPart"/> passthrough.
    /// </summary>
    Skeleton,
}

/// <summary>
/// Module metadata. Declared in code and mirrored in modules.json; the kernel
/// cross-checks the two so a manifest edit alone can disable a module.
/// </summary>
public sealed record ModuleManifest(
    string Id,
    string Version,
    string ContractsVersion,
    ModuleTier Tier = ModuleTier.Full,
    IReadOnlyList<string>? RequiresCapabilities = null,
    IReadOnlyList<string>? OptionalCapabilities = null,
    string DisplayName = "")
{
    public IReadOnlyList<string> Requires => RequiresCapabilities ?? Array.Empty<string>();
    public IReadOnlyList<string> Optional => OptionalCapabilities ?? Array.Empty<string>();
}

/// <summary>
/// A removable unit of editor functionality. A module is a registration unit,
/// not necessarily its own assembly: several related modules ship in one
/// domain assembly (Modules.Terrain, …) and are still individually removable
/// by dropping their modules.json entry.
///
/// Register() must be cheap and side-effect-free beyond registration: no file
/// IO, no map parsing, no window creation. Heavy work happens when a command
/// runs or a panel view is first created.
/// </summary>
public interface IEditorModule : IDisposable
{
    ModuleManifest Manifest { get; }
    void Register(IModuleContext ctx);
}

/// <summary>
/// Everything a module may touch. This is the ONLY legal surface — a module
/// that reaches around it (static singletons, direct references to another
/// module's assembly) breaks the removal guarantee.
/// </summary>
public interface IModuleContext
{
    /// <summary>Currently open document, or null when no map is open.</summary>
    IMapDocument? Document { get; }

    /// <summary>Fires when <see cref="Document"/> is replaced (open/close/new).</summary>
    event Action<IMapDocument?>? DocumentChanged;

    /// <summary>The only legal cross-module channel: query by interface, degrade if absent.</summary>
    ICapabilities Capabilities { get; }

    /// <summary>Structured log; Error/Warn surface in the UI status bar (external review 4.36).</summary>
    IEditorLog Log { get; }

    /// <summary>Id of the module this context belongs to (used to attribute contributions).</summary>
    string ModuleId { get; }

    void AddCommand(CommandDescriptor command);
    void AddTool(IToolDescriptor tool);
    void AddPanel(IPanelDescriptor panel);
    void AddWindow(IWindowDescriptor window);
    void AddOverlay(IViewportOverlay overlay);
    void AddPartCodec(IPartCodec codec);
    void AddAssetSource(IAssetSource source);

    /// <summary>Publishes a capability other modules may query. Keyed by interface type.</summary>
    void ProvideCapability<T>(T instance) where T : class;
}

/// <summary>
/// Cross-module capability lookup. Always handle the null case: the provider
/// module may have been removed.
/// </summary>
public interface ICapabilities
{
    T? Get<T>() where T : class;
    bool Has<T>() where T : class;
    IReadOnlyList<string> ProvidedNames { get; }
}

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Free-threaded structured log. Warn/Error must reach the user, not just stderr.</summary>
public interface IEditorLog
{
    void Write(LogLevel level, string message, Exception? error = null);
    void Debug(string message) => Write(LogLevel.Debug, message);
    void Info(string message) => Write(LogLevel.Info, message);
    void Warn(string message) => Write(LogLevel.Warn, message);
    void Error(string message, Exception? error = null) => Write(LogLevel.Error, message, error);
}
