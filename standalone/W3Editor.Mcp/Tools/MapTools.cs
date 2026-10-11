using System.ComponentModel;
using ModelContextProtocol.Server;

namespace W3Editor.Mcp.Tools;

[McpServerToolType]
public sealed class MapTools(EditorSession session)
{
    [McpServerTool(Name = "map_status"), Description("Current W3 editor session status: path, size, tileset, dirty, doodad/unit counts.")]
    public string MapStatus() => session.Locked(session.StatusJson);

    [McpServerTool(Name = "map_open"), Description("Open a map folder, war3map.w3e, or .w3x/.w3m archive (archives are unpacked offline).")]
    public string MapOpen(
        [Description("Absolute path to map folder, .w3e, .w3x, or .w3m")] string path)
        => session.Locked(() => session.Open(path));

    [McpServerTool(Name = "map_new"), Description("Create a blank HiveWE-sized map (each side 32–480 tiles, step 4) with optional tileset.")]
    public string MapNew(
        [Description("Map folder name")] string name = "mcp_map",
        [Description("Width in tiles (32–480, multiple of 4)")] int widthTiles = 64,
        [Description("Height in tiles (32–480, multiple of 4)")] int heightTiles = 64,
        [Description("Tileset letter, e.g. L=Lordaeron Summer, B=Barrens")] string tileset = "L",
        [Description("Optional initial ground tile fourcc, e.g. Lgrs")] string? initialTile = null)
    {
        char letter = string.IsNullOrWhiteSpace(tileset) ? 'L' : char.ToUpperInvariant(tileset.Trim()[0]);
        return session.Locked(() => session.CreateNew(name, widthTiles, heightTiles, letter, initialTile));
    }

    [McpServerTool(Name = "map_save"), Description("Save war3map.w3e and, for folder maps, war3map.doo / war3mapUnits.doo (refreshes .bak each save).")]
    public string MapSave()
    {
        return session.Locked(() =>
        {
            session.Save();
            return session.StatusJson();
        });
    }

    [McpServerTool(Name = "map_analyze"), Description(
        "Read-only structural dump of the open map: tileset, layer/texture histograms, water/ramp counts, top doodads/units, coarse grid.")]
    public string MapAnalyze(
        [Description("Coarse grid step in corners (2–32)")] int coarseStep = 8)
        => session.Locked(() => session.AnalyzeJson(coarseStep));

    [McpServerTool(Name = "rebuild_lost_temple_draft"), Description(
        "Create a new draft map from a Lost Temple reference folder: sync terrain corners from the reference, place trees/landmarks via place_object, save.")]
    public string RebuildLostTempleDraft(
        [Description("Absolute path to reference map folder (e.g. D:\\maps\\LostTemple)")] string referencePath,
        [Description("New map folder name")] string newMapName = "LostTemple_MCP")
        => session.Locked(() => session.RebuildLostTempleDraft(referencePath, newMapName, brushPolish: true));
}
