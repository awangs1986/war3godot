namespace W3Editor.Contracts;

/// <summary>
/// Contract surface version. The kernel refuses to load a module whose
/// <see cref="ModuleManifest.ContractsVersion"/> major differs from this
/// (external review 4.23: v1 had no handshake, so a stale plugin failed with
/// MissingMethodException mid-command instead of a clear load error).
///
/// Compatibility rule: NEVER modify a shipped interface. Add a new one
/// (IFooV2 / a capability interface) and let the kernel offer both.
/// </summary>
public static class ContractVersion
{
    public const int Major = 2;
    public const int Minor = 0;
    public const string Value = "2.0";

    /// <summary>True when a module built against <paramref name="declared"/> can load here.</summary>
    public static bool IsCompatible(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
            return false;
        string majorText = declared.Split('.', 2)[0];
        return int.TryParse(majorText, out int major) && major == Major;
    }
}

/// <summary>
/// Threading contract every module must honor. The kernel does not enforce
/// this with locks on the hot path; violating it corrupts documents.
///
/// UI thread (the thread that called <see cref="IEditorModule.Register"/>):
///   - <see cref="ITool"/> callbacks. Must be cheap: no IO, no parsing.
///   - <see cref="IPanelDescriptor.CreateView"/> and all view code.
///   - <see cref="IViewportOverlay.Render"/>.
/// Any thread, but one at a time per document:
///   - <see cref="IEditTransaction"/> — NOT thread-safe. One open transaction
///     per document; the kernel throws on a second concurrent BeginEdit.
///   - <see cref="IPartCodec"/> read/write.
/// Free-threaded:
///   - <see cref="CommandDescriptor.Handler"/> — invoked with a real
///     CancellationToken; long work belongs here, not in a tool callback
///     (external review 4.12: FBX import froze the UI thread for minutes).
///   - <see cref="IEditorLog"/>.
/// </summary>
public static class ThreadingContract
{
    public const string Summary =
        "ITool/IPanel/IViewportOverlay: UI thread, cheap. " +
        "IEditTransaction/IPartCodec: single-threaded per document. " +
        "CommandDescriptor.Handler/IEditorLog: any thread.";
}
