using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3Editor.Mcp;
using W3Editor.Mcp.Tools;

// CLI bridges (same EditorSession as MCP tools; logs on stderr, result on stdout):
//   --selftest <map>
//   --analyze <map> [out.json]
//   --rebuild-lt <reference-map> [newName]
//   --export-lt-ref [source] [outDir]   offline JSON/heightmap (never HiveWE)
//   --materialize-ref [refDir] [outDir] build map folder from JSON refs only
//   --panel-build-lt-ref [refDir] [mapName]  brush+place from ref JSON (no corner-copy)
//   --human-probe [outJson]                 human-operator capability checklist (not auto-build)
//   --seal-brush-dump [outDir]              W3Editor raise stamp → heights.json
//   --seal-dump-w3e <w3e|folder> [outDir]   load w3e → heights.json
//   --seal-brush-diff [a.json] [b.json] [out.json]
//   --seal-ground-tex [mapFolder] [outDir]  RealTileTexture vs HiveWE golden
//   --plugin-host-selftest [outDir]         DLL plugin host load (TDD gate)
//   --fbx-import-accept [outDir]            FBX scale + dual ownership write
//   --make-fbx-sample [outDir]              generate compliant FBX + 512 PBR textures
if (args.Length >= 1 && args[0] is "--selftest" or "--analyze" or "--rebuild-lt" or "--export-lt-ref" or "--materialize-ref" or "--panel-build-lt-ref" or "--human-probe"
    or "--seal-brush-dump" or "--seal-dump-w3e" or "--seal-brush-diff" or "--seal-ground-tex" or "--probe-atlas-alpha" or "--repro-raise2" or "--probe-tile-composite" or "--probe-flatten-smooth" or "--m2-accept" or "--native-roundtrip" or "--catalog-selftest" or "--d1map-accept" or "--plugin-host-selftest" or "--fbx-import-accept" or "--make-fbx-sample")
{
    try
    {
        return await CliMain.RunAsync(args);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("ERROR: " + ex);
        return 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    // stdout is reserved for MCP JSON-RPC; all logs go to stderr.
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<EditorSession>();
builder.Services.AddSingleton<MapTools>();
builder.Services.AddSingleton<BrushTools>();
builder.Services.AddSingleton<PlacementTools>();
builder.Services.AddSingleton<CaptureTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

internal static class CliMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        switch (args[0])
        {
            case "--selftest":
                return await SelfTest.RunAsync(args[1], CancellationToken.None);

            case "--analyze":
            {
                var session = new EditorSession();
                Console.Error.WriteLine(session.Open(args[1]));
                string json = session.AnalyzeJson(coarseStep: 8);
                string outPath = args.Length >= 3
                    ? Path.GetFullPath(args[2])
                    : W3Editor.Services.EditorPaths.OutFile("analyze.json");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllTextAsync(outPath, json);
                Console.Error.WriteLine("wrote " + outPath);
                Console.WriteLine(outPath);
                return 0;
            }

            case "--rebuild-lt":
            {
                var session = new EditorSession();
                string name = args.Length >= 3 ? args[2] : "LostTemple_MCP";
                string result = session.RebuildLostTempleDraft(args[1], name, brushPolish: true);
                Console.Error.WriteLine(result);
                Console.WriteLine(result);
                // Capture a GL look at the draft.
                try
                {
                    string png = await session.Screenshot3dAsync(null, CancellationToken.None);
                    Console.Error.WriteLine("screenshot " + png);
                    Console.WriteLine(png);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("screenshot skipped: " + ex.Message);
                }
                return 0;
            }

            case "--export-lt-ref":
                return ExportLostTempleRef.Run(args);

            case "--materialize-ref":
                return MaterializeFromRef.Run(args);

            case "--panel-build-lt-ref":
                return PanelBuildFromRef.Run(args);

            case "--human-probe":
                return HumanCapabilityProbe.Run(args);

            case "--seal-brush-dump":
                return SealBrushCompare.Dump(args);

            case "--seal-dump-w3e":
                return SealBrushCompare.DumpW3e(args);

            case "--seal-brush-diff":
                return SealBrushCompare.Diff(args);

            case "--seal-ground-tex":
                return SealGroundTextures.Run(args);

            case "--probe-atlas-alpha":
                return ProbeAtlasAlpha.Run(args);

            case "--repro-raise2":
                return ReproRaise2.Run(args);

            case "--probe-tile-composite":
                return ProbeTileComposite.Run(args);

            case "--probe-flatten-smooth":
                return await ProbeFlattenSmooth.RunAsync(args);

            case "--m2-accept":
                return M2Accept.Run(args);

            case "--native-roundtrip":
                return NativeRoundtrip.Run(args);

            case "--catalog-selftest":
                return CatalogSelfTest.Run(args);

            case "--d1map-accept":
                return D1MapAccept.Run(args);

            case "--plugin-host-selftest":
                return PluginHostSelfTest.Run(args);

            case "--fbx-import-accept":
                return FbxImportAccept.Run(args);

            case "--make-fbx-sample":
                return MakeFbxSample.Run(args);

            default:
                Console.Error.WriteLine("Unknown CLI mode");
                return 2;
        }
    }
}
