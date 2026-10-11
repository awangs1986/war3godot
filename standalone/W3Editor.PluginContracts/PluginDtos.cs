namespace W3Editor.PluginContracts;

public sealed class PluginMeshPreview
{
    public float[] Positions = Array.Empty<float>(); // xyz interleaved
    public float[] Normals = Array.Empty<float>();
    public float[] Uv0 = Array.Empty<float>();
    public int[] Indices = Array.Empty<int>();
    public string? AlbedoPath;
}

public enum PluginIssueSeverity { Info, Warn, Error }

public sealed class PluginValidationIssue
{
    public PluginIssueSeverity Severity;
    public string Code = "";
    public string Message = "";
}

public sealed class PluginValidationReport
{
    public string Ownership = ""; // "external" | "map-packed"
    public string TargetDir = "";
    public bool AutoScaled;
    public float ScaleFactor = 1f;
    public List<PluginValidationIssue> Issues = new();
}
