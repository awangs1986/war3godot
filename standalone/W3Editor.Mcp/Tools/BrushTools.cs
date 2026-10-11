using System.ComponentModel;
using ModelContextProtocol.Server;

namespace W3Editor.Mcp.Tools;

[McpServerToolType]
public sealed class BrushTools(EditorSession session)
{
    [McpServerTool(Name = "brush_stamp"), Description(
        "Apply a terrain brush stamp or line. Ops: Texture, Raise, Lower, Plateau, Smooth, " +
        "CliffLower2, CliffLower1, CliffLevel, CliffRaise1, CliffRaise2, WaterShallow, WaterDeep, Ramp. " +
        "Coordinates are corner-grid indices.")]
    public string BrushStamp(
        [Description("Brush op name")] string op,
        [Description("Corner X")] int cx,
        [Description("Corner Y")] int cy,
        [Description("Optional line end corner X")] int? toCx = null,
        [Description("Optional line end corner Y")] int? toCy = null,
        [Description("Radius in corners (1–32)")] int radius = 4,
        [Description("Strength 0–1")] float strength = 0.6f,
        [Description("Ground texture palette index for Texture op")] int textureIndex = 0,
        [Description("Erase / reverse (water remove, ramp clear, etc.)")] bool erase = false)
        => session.BrushStamp(op, cx, cy, toCx, toCy, radius, strength, textureIndex, erase);

    [McpServerTool(Name = "undo"), Description("Undo the last terrain stroke or placement edit.")]
    public string Undo() => session.Undo();

    [McpServerTool(Name = "redo"), Description("Redo the last undone edit.")]
    public string Redo() => session.Redo();
}
