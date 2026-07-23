using System.ComponentModel;
using ModelContextProtocol.Server;

namespace W3Editor.Mcp.Tools;

[McpServerToolType]
public sealed class PlacementTools(EditorSession session)
{
    [McpServerTool(Name = "catalog_search"), Description(
        "Search the doodad/destructible/unit catalog from SLKs (Id/Name/category).")]
    public string CatalogSearch(
        [Description("Name or rawcode substring")] string? query = null,
        [Description("all | doodad | unit")] string kind = "all",
        [Description("Optional doodad category letter: D trees, E environment, O obstacles, S structures, …")] string? category = null,
        [Description("Max results (1–200)")] int limit = 40)
    {
        char? cat = null;
        if (!string.IsNullOrWhiteSpace(category))
            cat = char.ToUpperInvariant(category.Trim()[0]);
        return session.CatalogSearch(query, kind, cat, limit);
    }

    [McpServerTool(Name = "list_placements"), Description("List or summarize placements on the open map.")]
    public string ListPlacements(
        [Description("all | doodad | unit")] string kind = "all",
        [Description("Optional rawcode filter")] string? id = null,
        [Description("Max listed items (1–500)")] int limit = 50)
        => session.ListPlacements(kind, id, limit);

    [McpServerTool(Name = "place_object"), Description(
        "Place a doodad or unit at W3 world X/Y (Z snapped to terrain). Uses undo stack.")]
    public string PlaceObject(
        [Description("Fourcc rawcode, e.g. LTlt")] string id,
        [Description("W3 world X")] float x,
        [Description("W3 world Y (north)")] float y,
        [Description("true = unit (war3mapUnits.doo), false = doodad")] bool isUnit = false,
        [Description("Model variation index")] int? variation = null,
        [Description("Facing in degrees (default 0 doodad / 270 unit)")] float? angleDeg = null,
        [Description("Uniform scale multiplier")] float? scale = null,
        [Description("Player owner 0–15 for units")] int owner = 0)
        => session.PlaceObject(id, x, y, isUnit, variation, angleDeg, scale, owner);

    [McpServerTool(Name = "remove_objects"), Description(
        "Remove placements by creationNumber (requires kind=doodad|unit — the number sequences collide across files), " +
        "or all within radius of world X/Y.")]
    public string RemoveObjects(
        [Description("Exact creation number (must scope with kind)")] int? creationNumber = null,
        [Description("World X center")] float? x = null,
        [Description("World Y center")] float? y = null,
        [Description("Search radius in world units (default 128)")] float radius = 128f,
        [Description("doodad | unit | any (any is invalid with creationNumber)")] string kind = "any")
        => session.RemoveObjects(creationNumber, x, y, radius, kind);
}
