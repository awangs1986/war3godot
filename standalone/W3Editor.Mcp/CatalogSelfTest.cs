using System.Text.Json;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// IObjectCatalog acceptance: build a native catalog.json, load it through
/// JsonObjectCatalog and assert the interface contract (lookup, model
/// resolution with variations, scales). Usage: --catalog-selftest [outDir]
/// </summary>
internal static class CatalogSelfTest
{
    public static int Run(string[] args)
    {
        string outDir = args.Length >= 2 ? args[1] : Path.Combine(EditorPaths.OutDir, "catalog_selftest");
        Directory.CreateDirectory(outDir);
        string jsonPath = Path.Combine(outDir, "catalog.json");
        File.WriteAllText(jsonPath, """
        {
          "format": "d1catalog", "version": 1,
          "doodads": [
            { "id": "tree_01", "name": "枯树", "category": "D",
              "models": ["models/tree_01a.glb", "models/tree_01b.glb", "models/tree_01c.glb"],
              "defScale": 1.5, "minScale": 0.8, "maxScale": 1.4, "randomScale": true,
              "fixedRot": -1, "texFile": "textures/tree_01_bark.png" },
            { "id": "rock_01", "name": "岩石", "category": "O",
              "models": ["models/rock_01.glb"] }
          ],
          "units": [
            { "id": "skeleton", "name": "骷髅兵", "model": "models/skeleton.glb",
              "scale": 1.25, "race": "monster", "icon": "icons/skeleton.png" }
          ]
        }
        """);

        IObjectCatalog catalog = new JsonObjectCatalog(jsonPath);

        var checks = new Dictionary<string, bool>
        {
            ["doodadCount"] = catalog.Doodads.Count == 2,
            ["unitCount"] = catalog.Units.Count == 1,
            ["findDoodad"] = catalog.FindDoodad("tree_01")?.Name == "枯树",
            ["findMissing"] = catalog.FindDoodad("nope") == null,
            ["category"] = catalog.FindDoodad("rock_01")?.Category == 'O',
            ["numVarFromModels"] = catalog.FindDoodad("tree_01")?.NumVar == 3,
            ["resolveVariation1"] = catalog.ResolveModelRel("tree_01", isUnit: false, variation: 1) == "models/tree_01b.glb",
            ["resolveVariationClamped"] = catalog.ResolveModelRel("tree_01", isUnit: false, variation: 9) == "models/tree_01c.glb",
            ["resolveUnit"] = catalog.ResolveModelRel("skeleton", isUnit: true, variation: 0) == "models/skeleton.glb",
            ["resolveUnknown"] = catalog.ResolveModelRel("nope", isUnit: false, variation: 0) == "",
            ["doodadScale"] = Math.Abs(catalog.ModelScale("tree_01", isUnit: false) - 1.5f) < 1e-5f,
            ["unitScale"] = Math.Abs(catalog.ModelScale("skeleton", isUnit: true) - 1.25f) < 1e-5f,
            ["texFile"] = catalog.DestructibleTexFile("tree_01") == "textures/tree_01_bark.png",
            ["randomScale"] = catalog.FindDoodad("tree_01")?.RandomScale == true,
        };

        bool pass = checks.Values.All(v => v);
        string json = JsonSerializer.Serialize(
            new { catalogJson = jsonPath, checks, pass },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "catalog_selftest.json"), json);
        Console.WriteLine(json);
        return pass ? 0 : 1;
    }
}
