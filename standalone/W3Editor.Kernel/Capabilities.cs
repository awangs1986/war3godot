using W3Editor.Contracts;

namespace W3Editor.Kernel;

/// <summary>Type-keyed capability registry — the only sanctioned cross-module channel.</summary>
public sealed class CapabilityRegistry : ICapabilities
{
    private readonly Dictionary<Type, object> _items = new();
    private readonly Dictionary<Type, string> _providers = new();

    public void Provide<T>(T instance, string providerModuleId) where T : class
    {
        if (_items.TryGetValue(typeof(T), out _))
            throw new InvalidOperationException(
                $"Capability {typeof(T).Name} is already provided by '{_providers[typeof(T)]}'.");
        _items[typeof(T)] = instance;
        _providers[typeof(T)] = providerModuleId;
    }

    public T? Get<T>() where T : class =>
        _items.TryGetValue(typeof(T), out object? value) ? (T)value : null;

    public bool Has<T>() where T : class => _items.ContainsKey(typeof(T));

    public bool Has(string typeFullName) =>
        _items.Keys.Any(t => string.Equals(t.FullName, typeFullName, StringComparison.Ordinal));

    public IReadOnlyList<string> ProvidedNames =>
        _items.Keys.Select(t => t.FullName ?? t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>Drops everything a module provided (used when unloading it).</summary>
    public void RemoveProvider(string moduleId)
    {
        foreach (Type type in _providers.Where(p => p.Value == moduleId).Select(p => p.Key).ToList())
        {
            _items.Remove(type);
            _providers.Remove(type);
        }
    }
}

/// <summary>Console + subscriber log. Warn/Error are what the shell shows the user.</summary>
public sealed class EditorLog : IEditorLog
{
    private readonly List<(LogLevel Level, string Message)> _entries = new();
    private readonly object _sync = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public event Action<LogLevel, string, Exception?>? Logged;

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (_sync) return _entries.ToList(); }
    }

    // Concrete overloads: the contract's default interface members are only
    // reachable through IEditorLog, and the kernel holds the concrete type.
    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message, Exception? error = null) => Write(LogLevel.Error, message, error);

    public void Write(LogLevel level, string message, Exception? error = null)
    {
        if (level < MinimumLevel)
            return;
        lock (_sync)
        {
            _entries.Add((level, message));
            if (_entries.Count > 2000)
                _entries.RemoveRange(0, 500);
        }
        if (level >= LogLevel.Warn)
            Console.Error.WriteLine($"[{level}] {message}{(error != null ? " :: " + error.Message : "")}");
        Logged?.Invoke(level, message, error);
    }
}
