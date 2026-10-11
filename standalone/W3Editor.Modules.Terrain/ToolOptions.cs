using W3Editor.Contracts;

namespace W3Editor.Modules.Terrain;

/// <summary>
/// Declarative option bag. The shell renders the widgets from
/// <see cref="Schema"/>, so no module needs a UI dependency to expose settings.
/// </summary>
public sealed class ToolOptions : IToolOptions
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public ToolOptions(params ToolOption[] schema)
    {
        Schema = schema;
        foreach (ToolOption option in schema)
            _values[option.Key] = option.Default;
    }

    public IReadOnlyList<ToolOption> Schema { get; }
    public event Action<string>? OptionChanged;

    public object? Get(string key) => _values.TryGetValue(key, out object? v) ? v : null;

    public void Set(string key, object? value)
    {
        if (!_values.ContainsKey(key))
            throw new ArgumentException($"Unknown option '{key}'.", nameof(key));
        if (Equals(_values[key], value))
            return;
        _values[key] = value;
        OptionChanged?.Invoke(key);
    }

    public int GetInt(string key, int fallback = 0) =>
        Get(key) switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out int p) => p,
            _ => fallback,
        };

    public float GetFloat(string key, float fallback = 0f) =>
        Get(key) switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s when float.TryParse(s, out float p) => p,
            _ => fallback,
        };

    public bool GetBool(string key, bool fallback = false) =>
        Get(key) switch
        {
            bool b => b,
            string s when bool.TryParse(s, out bool p) => p,
            _ => fallback,
        };

    public string GetString(string key, string fallback = "") =>
        Get(key)?.ToString() ?? fallback;
}
