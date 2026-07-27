using System.Text.Json;
using W3Editor.Contracts;
using W3Editor.Formats;
using W3Editor.Kernel;
using W3Editor.Modules.Placement;
using W3Editor.Modules.Data;
using W3Editor.Modules.Objects;
using W3Editor.Modules.Terrain;
using W3Editor.Modules.Ui;
using W3Editor.Modules.WorldData;
using W3Editor.Services;
// W3Editor.Services has its own MapFormatKind (the pre-module MapSession one).
// The contracts enum is the module-facing type; alias so the two never blur.
using MapFormatKind = W3Editor.Contracts.MapFormatKind;

namespace W3Editor.Mcp;

/// <summary>
/// Acceptance for the module architecture (contracts v2 + kernel).
///
/// Proves the claims the design rests on, not merely that code runs:
///   - existing terrain/placement functionality survives the contract,
///   - all map files commit as ONE generation and a torn set is detected,
///   - undo through the kernel restores the original bytes,
///   - the shape fingerprint rejects a stale delta instead of corrupting,
///   - removing a module removes its UI contributions,
///   - removing a module does NOT lose its data (passthrough).
///
/// Asset-free: runs against testdata/hivewe_sample, so it belongs in CI.
/// Usage: --module-host-accept [sourceMapFolder] [workDir]
/// </summary>
internal static class ModuleHostAccept
{
    public static int Run(string[] args)
    {
        string root = GodotLauncher.FindProjectRoot()
            ?? throw new InvalidOperationException("project root (project.godot) not found");
        string source = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(root, "testdata", "hivewe_sample");
        string work = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(EditorPaths.OutDir, "module_host_accept");
        string manifestPath = Path.Combine(root, "standalone", "modules.json");

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"source map folder not found: {source}");
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        CopyFolder(source, work);

        // A file no codec claims. Real maps always have several (w3i, wpm, …);
        // the committed sample does not, and passthrough is the guarantee that
        // makes module removal data-safe, so it must be exercised here.
        File.WriteAllBytes(
            Path.Combine(work, "module-host-unknown.bin"),
            new byte[] { 0xD1, 0x3A, 0x9E, 0x01, 0x00, 0xFF, 0x42, 0x17 });
        string nestedUnknown = Path.Combine(work, "imports", "custom", "payload.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedUnknown)!);
        File.WriteAllBytes(nestedUnknown, new byte[] { 9, 7, 5, 3, 1, 0, 2, 4, 6, 8 });

        var originals = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetRelativePath(work, p).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, object?>
        {
            ["source"] = source,
            ["work"] = work,
            ["manifest"] = manifestPath,
        };

        // ---------------------------------------------------------------
        // 1. Load modules and open the map entirely through the contract.
        // ---------------------------------------------------------------
        using var host = new ModuleHost();
        PrepareHeadless(host);
        ModuleLoadReport report = host.Load(manifestPath, new[]
        {
            typeof(TerrainPartModule).Assembly,
            typeof(DoodadsPartModule).Assembly,
        });
        result["modulesLoaded"] = report.Loaded.OrderBy(x => x, StringComparer.Ordinal).ToList();
        result["modulesDisabledCount"] = report.Disabled.Count;

        // Every enabled module in the manifest must actually have loaded.
        ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
        var expected = manifest.Modules.Where(m => m.Enabled).Select(m => m.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        bool allEnabledLoaded = expected.SequenceEqual(report.Loaded.OrderBy(x => x, StringComparer.Ordinal));
        result["allEnabledModulesLoaded"] = allEnabledLoaded;
        result["manifestTotalModules"] = manifest.Modules.Count;

        MapDocument doc = host.OpenDocument(work, MapFormatKind.War3Folder);
        var terrain = doc.GetPart<TerrainPart>();
        var doodads = doc.GetPart<DoodadsPart>();
        var units = doc.GetPart<UnitsPart>();
        result["partsLoaded"] = doc.Parts.Select(p => p.PartId).OrderBy(x => x, StringComparer.Ordinal).ToList();
        result["passthroughFiles"] = doc.UnknownParts.Select(u => u.FileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
        bool corePartsPresent = terrain != null && doodads != null && units != null;
        result["corePartsPresent"] = corePartsPresent;

        // ---------------------------------------------------------------
        // 2. Apply a terrain stroke through ITool, inside a transaction.
        // ---------------------------------------------------------------
        var toolDescriptor = host.Registry.Tools.First(t => t.Id == "tool.terrain.height");
        ITool tool = toolDescriptor.Create(FakeContext(host));
        ((ToolOptions)tool.Options!).Set("mode", "Raise");
        ((ToolOptions)tool.Options!).Set("radius", 4);

        // Probe points are derived from the map, so the gate works on the small
        // committed sample and on a full-size map alike.
        int cx = terrain!.Map.Width / 2;
        int cy = terrain.Map.Height / 2;
        int qx = Math.Max(1, terrain.Map.Width / 4);
        int qy = Math.Max(1, terrain.Map.Height / 4);
        int rx = Math.Min(terrain.Map.Width - 2, terrain.Map.Width * 3 / 4);
        int ry = Math.Min(terrain.Map.Height - 2, terrain.Map.Height * 3 / 4);
        result["mapCorners"] = $"{terrain.Map.Width}x{terrain.Map.Height}";

        float heightBefore = terrain.Map.Corners[cy * terrain.Map.Width + cx].GroundHeight;
        using (IEditTransaction tx = doc.BeginEdit("raise"))
        {
            var strokeCtx = new ToolStrokeContext { Document = doc, Transaction = tx, X = cx, Y = cy };
            tool.OnBegin(strokeCtx);
            tool.OnApply(strokeCtx, 0.5);
            tool.OnEnd(strokeCtx);
            result["toolReportedTouchedRect"] = strokeCtx.TouchedRect != null;
            tx.Commit();
        }
        float heightAfter = terrain.Map.Corners[cy * terrain.Map.Width + cx].GroundHeight;
        result["strokeChangedTerrain"] = Math.Abs(heightAfter - heightBefore) > 1e-6f;
        result["historyHasUndo"] = host.History.CanUndo;
        result["historyRetainedBytes"] = host.History.RetainedBytes;

        // ---------------------------------------------------------------
        // 3. Save: one generation across all files.
        // ---------------------------------------------------------------
        doc.Save();
        long gen1 = doc.Generation;
        AtomicFileSet.VerifyResult verify1 = AtomicFileSet.Verify(work);
        result["generationAfterFirstSave"] = gen1;
        result["generationVerifiesClean"] = verify1.Ok;
        result["terrainBytesChangedOnDisk"] =
            !File.ReadAllBytes(Path.Combine(work, "war3map.w3e")).SequenceEqual(originals["war3map.w3e"]);

        // Files nobody claimed must be re-emitted byte-for-byte.
        bool passthroughIntact = doc.UnknownParts.All(u =>
            originals.TryGetValue(u.FileName, out byte[]? original)
            && File.ReadAllBytes(Path.Combine(work, u.FileName)).SequenceEqual(original));
        result["passthroughBytesIntact"] = passthroughIntact;
        // Guard against the check passing vacuously on a map with no unknowns.
        result["passthroughSawFiles"] = doc.UnknownParts.Count > 0;

        // ---------------------------------------------------------------
        // 4. Undo → save → original bytes restored.
        // ---------------------------------------------------------------
        bool undone = host.History.Undo();
        doc.Save();
        long gen2 = doc.Generation;
        bool restored = File.ReadAllBytes(Path.Combine(work, "war3map.w3e")).SequenceEqual(originals["war3map.w3e"]);
        result["undoApplied"] = undone;
        result["undoRestoredTerrainBytes"] = restored;
        result["generationAdvances"] = gen2 == gen1 + 1;

        // ---------------------------------------------------------------
        // 5. Rollback: an uncommitted transaction must leave no trace.
        // ---------------------------------------------------------------
        float beforeRollback = terrain.Map.Corners[ry * terrain.Map.Width + rx].GroundHeight;
        using (IEditTransaction tx = doc.BeginEdit("discarded"))
        {
            var strokeCtx = new ToolStrokeContext { Document = doc, Transaction = tx, X = rx, Y = ry };
            ITool t2 = toolDescriptor.Create(FakeContext(host));
            t2.OnBegin(strokeCtx);
            t2.OnApply(strokeCtx, 0.5);
            t2.OnEnd(strokeCtx);
            tx.Rollback();
        }
        result["rollbackRestoredState"] =
            Math.Abs(terrain.Map.Corners[ry * terrain.Map.Width + rx].GroundHeight - beforeRollback) < 1e-6f;

        // ---------------------------------------------------------------
        // 6. Shape fingerprint: a delta recorded against a different shape
        //    must invalidate history rather than write to relocated corners.
        // ---------------------------------------------------------------
        using (IEditTransaction tx = doc.BeginEdit("fingerprint probe"))
        {
            var strokeCtx = new ToolStrokeContext { Document = doc, Transaction = tx, X = qx, Y = qy };
            ITool t3 = toolDescriptor.Create(FakeContext(host));
            t3.OnBegin(strokeCtx);
            t3.OnApply(strokeCtx, 0.5);
            t3.OnEnd(strokeCtx);
            tx.Commit();
        }
        // Simulate a resize: the palette grows, so every corner index may move.
        terrain.Map.GroundTilesets.Add("Xfake");
        bool undoRefused = !host.History.Undo();
        result["staleShapeInvalidatesHistory"] = undoRefused && !host.History.CanUndo;
        terrain.Map.GroundTilesets.RemoveAt(terrain.Map.GroundTilesets.Count - 1);

        // ---------------------------------------------------------------
        // 7. Torn generation detection.
        // ---------------------------------------------------------------
        string tornDir = Path.Combine(work, "..", "module_host_accept_torn");
        tornDir = Path.GetFullPath(tornDir);
        if (Directory.Exists(tornDir))
            Directory.Delete(tornDir, recursive: true);
        Directory.CreateDirectory(tornDir);
        CopyFolder(work, tornDir);
        // The generation marker is the thing being tested here, so it has to
        // come along (CopyFolder deliberately skips kernel-owned files).
        File.Copy(
            Path.Combine(work, AtomicFileSet.MarkerFileName),
            Path.Combine(tornDir, AtomicFileSet.MarkerFileName),
            overwrite: true);
        result["tornFixtureHasMarker"] = AtomicFileSet.ReadMarker(tornDir) != null;
        // Rewrite one file behind the marker's back — exactly what a crash
        // between two renames leaves on disk.
        File.WriteAllBytes(Path.Combine(tornDir, "war3map.doo"), new byte[] { 1, 2, 3, 4 });
        AtomicFileSet.VerifyResult torn = AtomicFileSet.Verify(tornDir);
        result["tornGenerationDetected"] = !torn.Ok && torn.Problems.Count > 0;

        // ---------------------------------------------------------------
        // 8. Removal drill A: drop a tool module → its contribution vanishes,
        //    the rest keep working.
        // ---------------------------------------------------------------
        ModulesFile reduced = ModuleHost.ReadManifest(manifestPath);
        foreach (ModuleEntry entry in reduced.Modules.Where(m => m.Id == "tool.terrain.height"))
            entry.Enabled = false;
        using (var host2 = new ModuleHost())
        {
            host2.Load(reduced, new[] { typeof(TerrainPartModule).Assembly, typeof(DoodadsPartModule).Assembly });
            var toolIds = host2.Registry.Tools.Select(t => t.Id).ToList();
            result["removalDrillToolGone"] = !toolIds.Contains("tool.terrain.height");
            result["removalDrillOtherToolsAlive"] =
                toolIds.Contains("tool.terrain.cliff") && toolIds.Contains("tool.doodad");

            MapDocument doc2 = host2.OpenDocument(work, MapFormatKind.War3Folder);
            result["removalDrillMapStillOpens"] = doc2.GetPart<TerrainPart>() != null;
        }

        // ---------------------------------------------------------------
        // 9. Removal drill B: drop a DATA module → its file must survive as
        //    passthrough, byte-for-byte, across a full open/save cycle.
        // ---------------------------------------------------------------
        string dropDir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_droppart"));
        if (Directory.Exists(dropDir))
            Directory.Delete(dropDir, recursive: true);
        Directory.CreateDirectory(dropDir);
        CopyFolder(source, dropDir);
        byte[] dooOriginal = File.ReadAllBytes(Path.Combine(dropDir, "war3map.doo"));

        ModulesFile noDoodads = ModuleHost.ReadManifest(manifestPath);
        foreach (ModuleEntry entry in noDoodads.Modules.Where(m => m.Id == "part.doodads"))
            entry.Enabled = false;
        using (var host3 = new ModuleHost())
        {
            host3.Load(noDoodads, new[] { typeof(TerrainPartModule).Assembly, typeof(DoodadsPartModule).Assembly });
            MapDocument doc3 = host3.OpenDocument(dropDir, MapFormatKind.War3Folder);
            result["removalDrillPartAbsent"] = doc3.GetPart<DoodadsPart>() == null;
            result["removalDrillFileBecamePassthrough"] =
                doc3.UnknownParts.Any(u => u.FileName.Equals("war3map.doo", StringComparison.OrdinalIgnoreCase));

            // Edit something else, save, and confirm the orphaned file is intact.
            var t3 = doc3.GetPart<TerrainPart>()!;
            using (IEditTransaction tx = doc3.BeginEdit("edit without doodads module"))
            {
                var strokeCtx = new ToolStrokeContext
                {
                    Document = doc3,
                    Transaction = tx,
                    X = t3.Map.Width / 2,
                    Y = t3.Map.Height / 2,
                };
                ITool t = host3.Registry.Tools.First(x => x.Id == "tool.terrain.cliff").Create(FakeContext(host3));
                t.OnBegin(strokeCtx);
                t.OnApply(strokeCtx, 0.5);
                t.OnEnd(strokeCtx);
                tx.Commit();
            }
            doc3.Save();
            result["removalDrillDataSurvived"] =
                File.ReadAllBytes(Path.Combine(dropDir, "war3map.doo")).SequenceEqual(dooOriginal);
        }

        // ---------------------------------------------------------------
        // 10. Tier-B matrix: every passthrough codec must claim and reproduce
        //     all of its HiveWE-owned files byte-for-byte. Disabling the part
        //     must turn those files into UnknownParts without changing bytes.
        // ---------------------------------------------------------------
        (bool tierBClaimed, bool tierBRoundtrip, bool tierBRemoval) =
            RunTierBMatrix(source, work, manifestPath);
        result["tierBPartsClaimed"] = tierBClaimed;
        result["tierBBytesRoundtrip"] = tierBRoundtrip;
        result["tierBRemovalMatrix"] = tierBRemoval;

        (bool pathingLoaded, bool pathingChanged, bool pathingUndo) =
            RunPathingToolSmoke(source, work, manifestPath);
        result["pathingLoaded"] = pathingLoaded;
        result["pathingChanged"] = pathingChanged;
        result["pathingUndo"] = pathingUndo;

        (bool terrainPathingBlit, bool terrainPathingUndo) =
            RunTerrainPathingBlitSmoke(source, work, manifestPath);
        result["terrainPathingBlit"] = terrainPathingBlit;
        result["terrainPathingUndo"] = terrainPathingUndo;

        (bool regionsLoaded, bool regionsChanged, bool regionsUndo) =
            RunRegionToolSmoke(source, work, manifestPath);
        result["regionsLoaded"] = regionsLoaded;
        result["regionsChanged"] = regionsChanged;
        result["regionsUndo"] = regionsUndo;

        (bool tileIdPaint, bool boundaryEdit, bool rippleMatchesHiveWe) =
            RunTerrainFeatureSmoke(source, work, manifestPath);
        result["tileIdPaint"] = tileIdPaint;
        result["boundaryEdit"] = boundaryEdit;
        result["rippleMatchesHiveWeNoOp"] = rippleMatchesHiveWe;

        bool traversalRejected;
        try
        {
            AtomicFileSet.Commit(work, new[]
            {
                new AtomicFileSet.FilePayload("../escape.bin", new byte[] { 1 }),
            });
            traversalRejected = false;
        }
        catch (InvalidDataException)
        {
            traversalRejected = true;
        }
        result["pathTraversalRejected"] = traversalRejected;

        (bool mapInfoRoundtrip, bool mapInfoEditPreservesTail) =
            RunMapInfoSmoke(source, work, manifestPath);
        result["mapInfoRoundtrip"] = mapInfoRoundtrip;
        result["mapInfoEditPreservesTail"] = mapInfoEditPreservesTail;

        bool heightmapHiveWeFormula = RunHeightmapFormulaSmoke();
        result["heightmapHiveWeFormula"] = heightmapHiveWeFormula;
        (bool heightmapReferenceWithin15Percent, double heightmapReferenceMaePercent,
            double heightmapReferenceMaxError) = RunHeightmapReferenceSmoke(work);
        result["heightmapReferenceWithin15Percent"] = heightmapReferenceWithin15Percent;
        result["heightmapReferenceMaePercent"] = heightmapReferenceMaePercent;
        result["heightmapReferenceMaxError"] = heightmapReferenceMaxError;

        bool panelRemovalMatrix = RunPanelRemovalMatrix(manifestPath);
        result["panelRemovalMatrix"] = panelRemovalMatrix;
        bool shellCommandRemovalMatrix = RunShellCommandRemovalMatrix(manifestPath);
        result["shellCommandRemovalMatrix"] = shellCommandRemovalMatrix;
        bool allModuleRemovalMatrix = RunAllModuleRemovalMatrix(manifestPath);
        result["allModuleRemovalMatrix"] = allModuleRemovalMatrix;
        (bool cameraRoundtrip, bool cameraRemovalPassthrough, bool d1mapRoundtrip, bool d1mapRemovalPassthrough) =
            RunAdditionalPartSmokes(work, manifestPath);
        result["cameraRoundtrip"] = cameraRoundtrip;
        result["cameraRemovalPassthrough"] = cameraRemovalPassthrough;
        result["d1mapPartRoundtrip"] = d1mapRoundtrip;
        result["d1mapRemovalPassthrough"] = d1mapRemovalPassthrough;
        bool resizeMatchesHiveWe = RunResizeSmoke(source, work);
        result["resizeMatchesHiveWe"] = resizeMatchesHiveWe;

        // ---------------------------------------------------------------
        bool pass =
            allEnabledLoaded
            && corePartsPresent
            && Is(result, "toolReportedTouchedRect")
            && Is(result, "strokeChangedTerrain")
            && Is(result, "historyHasUndo")
            && Is(result, "generationVerifiesClean")
            && Is(result, "terrainBytesChangedOnDisk")
            && Is(result, "passthroughSawFiles")
            && Is(result, "passthroughBytesIntact")
            && Is(result, "tornFixtureHasMarker")
            && Is(result, "undoApplied")
            && Is(result, "undoRestoredTerrainBytes")
            && Is(result, "generationAdvances")
            && Is(result, "rollbackRestoredState")
            && Is(result, "staleShapeInvalidatesHistory")
            && Is(result, "tornGenerationDetected")
            && Is(result, "removalDrillToolGone")
            && Is(result, "removalDrillOtherToolsAlive")
            && Is(result, "removalDrillMapStillOpens")
            && Is(result, "removalDrillPartAbsent")
            && Is(result, "removalDrillFileBecamePassthrough")
            && Is(result, "removalDrillDataSurvived")
            && Is(result, "tierBPartsClaimed")
            && Is(result, "tierBBytesRoundtrip")
            && Is(result, "tierBRemovalMatrix")
            && Is(result, "pathingLoaded")
            && Is(result, "pathingChanged")
            && Is(result, "pathingUndo")
            && Is(result, "terrainPathingBlit")
            && Is(result, "terrainPathingUndo")
            && Is(result, "regionsLoaded")
            && Is(result, "regionsChanged")
            && Is(result, "regionsUndo")
            && Is(result, "tileIdPaint")
            && Is(result, "boundaryEdit")
            && Is(result, "rippleMatchesHiveWeNoOp")
            && Is(result, "pathTraversalRejected")
            && Is(result, "mapInfoRoundtrip")
            && Is(result, "mapInfoEditPreservesTail")
            && Is(result, "heightmapHiveWeFormula")
            && Is(result, "heightmapReferenceWithin15Percent")
            && Is(result, "panelRemovalMatrix")
            && Is(result, "shellCommandRemovalMatrix")
            && Is(result, "allModuleRemovalMatrix")
            && Is(result, "cameraRoundtrip")
            && Is(result, "cameraRemovalPassthrough")
            && Is(result, "d1mapPartRoundtrip")
            && Is(result, "d1mapRemovalPassthrough")
            && Is(result, "resizeMatchesHiveWe");
        result["pass"] = pass;

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return pass ? 0 : 1;
    }

    private static bool RunHeightmapFormulaSmoke()
    {
        W3EMap map = W3EMap.CreateBlank(32, 32);
        HiveWeHeightmapImport.Apply(map, map.Width, map.Height, (x, y) =>
        {
            if (y == map.Height - 1)
                return x == map.Width - 1 ? (byte)255 : (byte)128;
            if (y == 0)
                return x == map.Width - 1 ? (byte)128 : (byte)0;
            return 64;
        });
        return Math.Abs(map.Corners[0].GroundHeight - 0f) < 1e-6f
            && Math.Abs(map.Corners[map.Width - 1].GroundHeight - 15.875f) < 1e-6f
            && Math.Abs(map.Corners[(map.Height - 1) * map.Width].GroundHeight - (-16f)) < 1e-6f
            && Math.Abs(map.Corners[^1].GroundHeight - 0f) < 1e-6f;
    }

    /// <summary>
    /// Exercises the same PNG decode + top-down red-channel path used by the UI,
    /// against a non-trivial reference surface.  Error is normalized MAE over
    /// the source height range; the byte heightmap has an unavoidable 1/8-unit
    /// quantization step, so this is a stronger check than four exact samples.
    /// </summary>
    private static (bool Within15Percent, double MaePercent, double MaxError)
        RunHeightmapReferenceSmoke(string work)
    {
        const int tileWidth = 64;
        const int tileHeight = 64;
        W3EMap imported = W3EMap.CreateBlank(tileWidth, tileHeight);
        int width = imported.Width;
        int height = imported.Height;
        var reference = new float[width * height];
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        string path = Path.Combine(work, "heightmap-reference.png");
        using (var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(
                   width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque)))
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Deliberately not aligned to 1/8 so quantization error is measured.
                float value = (x - 32) * 0.115f + (y - 32) * 0.065f
                    + MathF.Sin(x * 0.23f) * 1.8f
                    + MathF.Cos(y * 0.19f) * 1.1f;
                reference[y * width + x] = value;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
                byte red = (byte)Math.Clamp(
                    (int)MathF.Round(value * 8f + 128f, MidpointRounding.AwayFromZero),
                    0,
                    255);
                bitmap.SetPixel(x, height - 1 - y, new SkiaSharp.SKColor(red, red, red, 255));
            }

            using SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using SkiaSharp.SKData data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidDataException("Could not encode reference heightmap PNG.");
            using FileStream output = File.Create(path);
            data.SaveTo(output);
        }

        using (SkiaSharp.SKBitmap decoded = SkiaSharp.SKBitmap.Decode(path)
                   ?? throw new InvalidDataException("Could not decode reference heightmap PNG."))
        {
            HiveWeHeightmapImport.Apply(
                imported,
                decoded.Width,
                decoded.Height,
                (x, y) => decoded.GetPixel(x, y).Red);
        }

        double absoluteError = 0;
        double maxError = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            double error = Math.Abs(imported.Corners[i].GroundHeight - reference[i]);
            absoluteError += error;
            maxError = Math.Max(maxError, error);
        }

        double range = Math.Max(max - min, 1e-9f);
        double maePercent = absoluteError / reference.Length / range * 100.0;
        return (maePercent <= 15.0, maePercent, maxError);
    }

    private static bool RunPanelRemovalMatrix(string manifestPath)
    {
        string[] panelIds =
        {
            "panel.terrain",
            "panel.doodads",
            "panel.units",
            "panel.minimap",
            "panel.pathing",
            "panel.regions",
        };

        foreach (string removedId in panelIds)
        {
            ModulesFile reduced = ModuleHost.ReadManifest(manifestPath);
            foreach (ModuleEntry entry in reduced.Modules.Where(module => module.Id == removedId))
                entry.Enabled = false;

            using var reducedHost = new ModuleHost();
            reducedHost.Load(reduced, new[]
            {
                typeof(TerrainPartModule).Assembly,
                typeof(DoodadsPartModule).Assembly,
            });
            HashSet<string> actual = reducedHost.Registry.Panels
                .Select(panel => panel.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (actual.Contains(removedId))
                return false;
            if (panelIds.Where(id => id != removedId).Any(id => !actual.Contains(id)))
                return false;
        }
        return true;
    }

    private static bool RunShellCommandRemovalMatrix(string manifestPath)
    {
        string[] commandIds =
        {
            "op.newmap", "op.open", "op.save", "op.saveas", "op.export",
            "op.changetileset", "op.changetilepathing",
            "dialog.tilesetter", "dialog.tilepicker", "dialog.tilepather",
            "view.camera", "render.terrain", "render.water", "render.cliff",
            "dialog.mapinfo", "op.importheightmap", "part.tilesets", "op.resize",
            "op.unusedfiles", "op.copypaste", "op.viewhistory", "op.testmap",
            "view.visibility", "dialog.settings", "manager.asset", "format.d1map",
            "bridge.godot", "import.fbx", "host.plugin", "assets.pack", "render.pbr",
        };

        foreach (string removedId in commandIds)
        {
            ModulesFile reduced = ModuleHost.ReadManifest(manifestPath);
            foreach (ModuleEntry entry in reduced.Modules.Where(module => module.Id == removedId))
                entry.Enabled = false;

            using var reducedHost = new ModuleHost();
            reducedHost.Load(reduced, new[]
            {
                typeof(TerrainPartModule).Assembly,
                typeof(DoodadsPartModule).Assembly,
            });
            HashSet<string> actual = reducedHost.Registry.Commands
                .Select(command => command.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (actual.Contains(removedId))
                return false;
            if (commandIds.Where(id => id != removedId).Any(id => !actual.Contains(id)))
                return false;
        }
        return true;
    }

    private static System.Reflection.Assembly[] AllModuleAssemblies() => new[]
    {
        typeof(TerrainPartModule).Assembly,
        typeof(DoodadsPartModule).Assembly,
        typeof(SoundsPartModule).Assembly,
        typeof(PathingPartModule).Assembly,
        typeof(TerrainPanelModule).Assembly,
        typeof(ObjectEditorModule).Assembly,
        typeof(NewMapOperationModule).Assembly,
    };

    private static void PrepareHeadless(ModuleHost host) =>
        host.CapabilityRegistry.Provide<IWindowService>(new HeadlessWindowService(), "accept.harness");

    private sealed class HeadlessWindowService : IWindowService
    {
        public bool CanOpen(string windowId) => true;
        public Task OpenAsync(string windowId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static byte[] BuildTierBFixture(string file, int sequence)
    {
        if (file.Equals("war3map.w3s", StringComparison.OrdinalIgnoreCase)
            || file.Equals("war3map.imp", StringComparison.OrdinalIgnoreCase))
            return new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        if (file.EndsWith(".w3u", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3t", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3a", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3b", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3q", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".w3h", StringComparison.OrdinalIgnoreCase))
            return new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        return System.Text.Encoding.UTF8.GetBytes($"tier-b:{sequence}:{file}\r\n\0\u00ff");
    }

    private static bool RunAllModuleRemovalMatrix(string manifestPath)
    {
        ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
        string[] expected = manifest.Modules.Select(module => module.Id).ToArray();
        foreach (string removedId in expected)
        {
            ModulesFile reduced = ModuleHost.ReadManifest(manifestPath);
            reduced.Modules.Single(module => module.Id == removedId).Enabled = false;
            using var host = new ModuleHost();
            PrepareHeadless(host);
            ModuleLoadReport report = host.Load(reduced, AllModuleAssemblies());
            if (report.Skipped.Count != 0 || report.Loaded.Contains(removedId, StringComparer.Ordinal))
                return false;
            if (expected.Where(id => id != removedId).Any(id => !report.Loaded.Contains(id, StringComparer.Ordinal)))
                return false;
        }
        return true;
    }

    private static (bool CameraRoundtrip, bool CameraRemoval, bool D1Roundtrip, bool D1Removal)
        RunAdditionalPartSmokes(string work, string manifestPath)
    {
        string root = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_extra_parts"));
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string war3 = Path.Combine(root, "war3");
        string d1 = Path.Combine(root, "d1");
        Directory.CreateDirectory(war3);
        Directory.CreateDirectory(d1);
        byte[] camera = { (byte)'W', (byte)'3', (byte)'C', 0, 5, 0, 0, 0, 0xD1, 0x7A };
        File.WriteAllBytes(Path.Combine(war3, "war3map.w3c"), camera);
        var d1Files = new Dictionary<string, byte[]>
        {
            ["map.json"] = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"d1map\",\"version\":1}"),
            ["terrain.bin"] = new byte[] { 1, 3, 3, 7 },
            ["placements.bin"] = new byte[] { 2, 4, 6, 8 },
        };
        foreach ((string name, byte[] bytes) in d1Files)
            File.WriteAllBytes(Path.Combine(d1, name), bytes);

        bool CameraCheck(bool disabled)
        {
            ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
            if (disabled) manifest.Modules.Single(module => module.Id == "part.cameras").Enabled = false;
            using var host = new ModuleHost();
            host.Load(manifest, AllModuleAssemblies());
            MapDocument doc = host.OpenDocument(war3, MapFormatKind.War3Folder);
            if (disabled)
                return doc.GetPart("cameras") == null && doc.UnknownParts.Any(file =>
                    file.FileName.Equals("war3map.w3c", StringComparison.OrdinalIgnoreCase) && file.Bytes.SequenceEqual(camera));
            doc.Save();
            return doc.GetPart<RawBundlePart>()?.Files["war3map.w3c"].SequenceEqual(camera) == true
                && File.ReadAllBytes(Path.Combine(war3, "war3map.w3c")).SequenceEqual(camera);
        }

        bool D1Check(bool disabled)
        {
            ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
            if (disabled) manifest.Modules.Single(module => module.Id == "part.d1map").Enabled = false;
            using var host = new ModuleHost();
            host.Load(manifest, AllModuleAssemblies());
            MapDocument doc = host.OpenDocument(d1, MapFormatKind.D1MapFolder);
            if (disabled)
                return doc.GetPart("d1map") == null && d1Files.All(expected => doc.UnknownParts.Any(file =>
                    file.FileName.Equals(expected.Key, StringComparison.OrdinalIgnoreCase) && file.Bytes.SequenceEqual(expected.Value)));
            doc.Save();
            RawBundlePart? part = doc.Parts.OfType<RawBundlePart>().SingleOrDefault(item => item.PartId == "d1map");
            return part != null && d1Files.All(expected =>
                part.Files[expected.Key].SequenceEqual(expected.Value) &&
                File.ReadAllBytes(Path.Combine(d1, expected.Key)).SequenceEqual(expected.Value));
        }

        return (CameraCheck(false), CameraCheck(true), D1Check(false), D1Check(true));
    }

    private static bool RunResizeSmoke(string source, string work)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_resize"));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        CopyFolder(source, dir);
        W3EMap original = W3EMap.Load(Path.Combine(dir, "war3map.w3e"));
        var pathing = new W3PathingMap
        {
            Width = original.TileCountX * 4,
            Height = original.TileCountY * 4,
            Cells = Enumerable.Range(0, original.TileCountX * original.TileCountY * 16)
                .Select(index => (byte)(index % 251)).ToArray(),
        };
        File.WriteAllBytes(Path.Combine(dir, "war3map.wpm"), pathing.SaveBytes());
        var session = new W3Editor.Services.MapSession();
        session.Open(dir);
        float oldOffsetX = session.Map!.CenterOffsetX, oldOffsetY = session.Map.CenterOffsetY;
        W3Corner old00 = session.Map.Corners[0];
        byte oldPath00 = session.Pathing!.Cells[0];
        session.Resize(32, 32);
        int left = (32 - original.TileCountX) / 2;
        int bottom = (32 - original.TileCountY) / 2;
        W3Corner mapped = session.Map.GetCorner(left, bottom)!;
        return session.Map.Width == 33 && session.Map.Height == 33
            && session.Pathing.Width == 128 && session.Pathing.Height == 128
            && Math.Abs(session.Map.CenterOffsetX - (oldOffsetX - left * W3EMap.TileSize)) < 0.01f
            && Math.Abs(session.Map.CenterOffsetY - (oldOffsetY - bottom * W3EMap.TileSize)) < 0.01f
            && Math.Abs(mapped.GroundHeight - old00.GroundHeight) < 0.0001f
            && session.Pathing.Cells[(bottom * 4) * session.Pathing.Width + left * 4] == oldPath00
            && session.Map.Corners[0].MapEdge && session.Map.Corners[^1].MapEdge;
    }

    private static (bool Roundtrip, bool EditPreservesTail) RunMapInfoSmoke(
        string source,
        string work,
        string manifestPath)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_mapinfo"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        CopyFolder(source, dir);

        byte[] tail = { 0x10, 0x00, 0xEE, 0x7F, 0x01, 0x02, 0x03, 0x04 };
        byte[] fixture;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(33); writer.Write(9); writer.Write(6116);
            writer.Write(2); writer.Write(0); writer.Write(3); writer.Write(22978);
            foreach (string value in new[] { "Map", "Author", "Description", "1-4" })
            {
                writer.Write(System.Text.Encoding.UTF8.GetBytes(value));
                writer.Write((byte)0);
            }
            writer.Write(tail);
            fixture = stream.ToArray();
        }
        string path = Path.Combine(dir, "war3map.w3i");
        File.WriteAllBytes(path, fixture);

        using var host = new ModuleHost();
        host.Load(manifestPath);
        MapDocument doc = host.OpenDocument(dir, MapFormatKind.War3Folder);
        MapInfoPart? info = doc.GetPart<MapInfoPart>();
        if (info == null)
            return (false, false);
        doc.Save();
        bool roundtrip = File.ReadAllBytes(path).SequenceEqual(fixture);
        info.Name = "Edited Map";
        info.MarkDirty();
        doc.Save();

        using var reloadHost = new ModuleHost();
        reloadHost.Load(manifestPath);
        MapInfoPart? reloaded = reloadHost.OpenDocument(dir, MapFormatKind.War3Folder)
            .GetPart<MapInfoPart>();
        bool preserved = reloaded != null
            && reloaded.Name == "Edited Map"
            && reloaded.OpaqueTail.SequenceEqual(tail);
        return (roundtrip, preserved);
    }

    private static (bool TileIdPaint, bool BoundaryEdit, bool RippleNoOp) RunTerrainFeatureSmoke(
        string source,
        string work,
        string manifestPath)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_terrain_features"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        CopyFolder(source, dir);

        using var host = new ModuleHost();
        host.Load(manifestPath);
        MapDocument doc = host.OpenDocument(dir, MapFormatKind.War3Folder);
        TerrainPart terrain = doc.GetPart<TerrainPart>()!;
        int x = terrain.Map.Width / 2;
        int y = terrain.Map.Height / 2;
        int index = y * terrain.Map.Width + x;

        int targetTexture = terrain.Map.GroundTilesets.Count > 1 ? 1 : 0;
        terrain.Map.Corners[index].GroundTexture = targetTexture == 0 ? 1 : 0;
        ITool texture = host.Registry.Tools.First(t => t.Id == "tool.terrain.texture").Create(FakeContext(host));
        texture.Options!.Set("radius", 1);
        texture.Options.Set("shape", "Square");
        texture.Options.Set("tileId", terrain.Map.GroundTilesets[targetTexture]);
        using (IEditTransaction tx = doc.BeginEdit("tile id paint"))
        {
            var stroke = new ToolStrokeContext { Document = doc, Transaction = tx, X = x, Y = y };
            texture.OnBegin(stroke); texture.OnApply(stroke, 0.5); texture.OnEnd(stroke); tx.Commit();
        }
        bool tileIdPaint = terrain.Map.Corners[index].GroundTexture == targetTexture;
        tileIdPaint &= host.History.Undo();

        terrain.Map.Corners[index].Boundary = false;
        ITool cell = host.Registry.Tools.First(t => t.Id == "tool.terrain.cell").Create(FakeContext(host));
        cell.Options!.Set("radius", 1);
        cell.Options.Set("shape", "Square");
        cell.Options.Set("mode", "BoundaryAdd");
        using (IEditTransaction tx = doc.BeginEdit("boundary add"))
        {
            var stroke = new ToolStrokeContext { Document = doc, Transaction = tx, X = x, Y = y };
            cell.OnBegin(stroke); cell.OnApply(stroke, 0.5); cell.OnEnd(stroke); tx.Commit();
        }
        bool boundary = terrain.Map.Corners[index].Boundary;
        boundary &= host.History.Undo() && !terrain.Map.Corners[index].Boundary;

        byte[] beforeRipple = terrain.PackAll();
        ITool height = host.Registry.Tools.First(t => t.Id == "tool.terrain.height").Create(FakeContext(host));
        height.Options!.Set("mode", "Ripple");
        using (IEditTransaction tx = doc.BeginEdit("ripple no-op"))
        {
            var stroke = new ToolStrokeContext { Document = doc, Transaction = tx, X = x, Y = y };
            height.OnBegin(stroke); height.OnApply(stroke, 0.5); height.OnEnd(stroke); tx.Commit();
        }
        bool ripple = terrain.PackAll().SequenceEqual(beforeRipple);
        return (tileIdPaint, boundary, ripple);
    }

    private static (bool Loaded, bool Changed, bool Undo) RunRegionToolSmoke(
        string source,
        string work,
        string manifestPath)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_regions"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        CopyFolder(source, dir);

        byte[] fixture;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(5);
            writer.Write(1);
            writer.Write(-128f); writer.Write(-64f); writer.Write(256f); writer.Write(320f);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("Region 000")); writer.Write((byte)0);
            writer.Write(7);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RAhr"));
            writer.Write(System.Text.Encoding.UTF8.GetBytes("Ambient")); writer.Write((byte)0);
            writer.Write((byte)84); writer.Write((byte)122); writer.Write((byte)185); writer.Write((byte)255);
            fixture = stream.ToArray();
        }
        File.WriteAllBytes(Path.Combine(dir, "war3map.w3r"), fixture);

        using var host = new ModuleHost();
        host.Load(manifestPath);
        MapDocument doc = host.OpenDocument(dir, MapFormatKind.War3Folder);
        RegionsPart? part = doc.GetPart<RegionsPart>();
        bool loaded = part is { Regions.Count: 1 }
            && part.Regions[0].Name == "Region 000"
            && part.Regions[0].R == 185
            && part.Regions[0].B == 84;
        if (part == null)
            return (false, false, false);

        doc.Save();
        loaded &= File.ReadAllBytes(Path.Combine(dir, "war3map.w3r")).SequenceEqual(fixture);
        ITool tool = host.Registry.Tools.First(t => t.Id == "tool.region").Create(FakeContext(host));
        using (IEditTransaction tx = doc.BeginEdit("region smoke"))
        {
            var stroke = new ToolStrokeContext
            {
                Document = doc,
                Transaction = tx,
                Fx = 128f,
                Fy = 256f,
            };
            tool.OnBegin(stroke);
            stroke.Fx = 512f;
            stroke.Fy = 640f;
            tool.OnApply(stroke, 0.5);
            tool.OnEnd(stroke);
            tx.Commit();
        }
        bool changed = part.Regions.Count == 2
            && part.Regions[1].Name == "Region 001";
        doc.Save();
        bool undo = host.History.Undo();
        doc.Save();
        undo &= part.Regions.Count == 1
            && File.ReadAllBytes(Path.Combine(dir, "war3map.w3r")).SequenceEqual(fixture);
        return (loaded, changed, undo);
    }

    private static (bool Loaded, bool Changed, bool Undo) RunPathingToolSmoke(
        string source,
        string work,
        string manifestPath)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_pathing"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        CopyFolder(source, dir);

        W3EMap terrain = W3EMap.Load(Path.Combine(dir, "war3map.w3e"));
        var pathing = new W3PathingMap
        {
            Width = terrain.Width * 4,
            Height = terrain.Height * 4,
            Cells = new byte[terrain.Width * terrain.Height * 16],
        };
        File.WriteAllBytes(Path.Combine(dir, "war3map.wpm"), pathing.SaveBytes());
        byte[] original = File.ReadAllBytes(Path.Combine(dir, "war3map.wpm"));

        using var host = new ModuleHost();
        host.Load(manifestPath);
        MapDocument doc = host.OpenDocument(dir, MapFormatKind.War3Folder);
        PathingPart? part = doc.GetPart<PathingPart>();
        bool loaded = part != null;
        if (part == null)
            return (false, false, false);

        int index = 4 * part.Map.Width + 4;
        byte before = part.Map.Cells[index];
        ITool tool = host.Registry.Tools.First(t => t.Id == "tool.pathing").Create(FakeContext(host));
        tool.Options!.Set("radius", 0);
        tool.Options.Set("mask", W3PathingMap.Unwalkable);
        using (IEditTransaction tx = doc.BeginEdit("pathing smoke"))
        {
            var stroke = new ToolStrokeContext
            {
                Document = doc,
                Transaction = tx,
                X = 4,
                Y = 4,
            };
            tool.OnBegin(stroke);
            tool.OnApply(stroke, 0.5);
            tool.OnEnd(stroke);
            tx.Commit();
        }
        bool changed = part.Map.Cells[index] == (byte)(before | W3PathingMap.Unwalkable);
        doc.Save();
        bool undo = host.History.Undo();
        doc.Save();
        undo &= part.Map.Cells[index] == before
            && File.ReadAllBytes(Path.Combine(dir, "war3map.wpm")).SequenceEqual(original);
        return (loaded, changed, undo);
    }

    private static (bool Blit, bool Undo) RunTerrainPathingBlitSmoke(
        string source,
        string work,
        string manifestPath)
    {
        string dir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_terrain_pathing"));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        CopyFolder(source, dir);

        W3EMap terrain = W3EMap.Load(Path.Combine(dir, "war3map.w3e"));
        var grid = new W3PathingMap
        {
            Width = terrain.Width * 4,
            Height = terrain.Height * 4,
            Cells = new byte[terrain.Width * terrain.Height * 16],
        };
        File.WriteAllBytes(Path.Combine(dir, "war3map.wpm"), grid.SaveBytes());
        byte[] originalWpm = File.ReadAllBytes(Path.Combine(dir, "war3map.wpm"));

        using var host = new ModuleHost();
        host.Load(manifestPath);
        MapDocument doc = host.OpenDocument(dir, MapFormatKind.War3Folder);
        PathingPart? pathing = doc.GetPart<PathingPart>();
        TerrainPart? terrainPart = doc.GetPart<TerrainPart>();
        if (pathing == null || terrainPart == null)
            return (false, false);

        ITool tool = host.Registry.Tools.First(t => t.Id == "tool.terrain.cell").Create(FakeContext(host));
        tool.Options!.Set("mode", "BoundaryAdd");
        tool.Options.Set("radius", 1);
        int cx = terrainPart.Map.Width / 2;
        int cy = terrainPart.Map.Height / 2;
        using (IEditTransaction tx = doc.BeginEdit("terrain pathing blit"))
        {
            var stroke = new ToolStrokeContext
            {
                Document = doc,
                Transaction = tx,
                X = cx,
                Y = cy,
            };
            tool.OnBegin(stroke);
            tool.OnApply(stroke, 0.5);
            tool.OnEnd(stroke);
            tx.Commit();
        }

        bool changed = pathing.Map.Cells.Any(cell =>
            (cell & (W3PathingMap.Unwalkable | W3PathingMap.Unflyable | W3PathingMap.Unbuildable)) != 0);
        doc.Save();
        bool undo = host.History.Undo();
        doc.Save();
        undo &= File.ReadAllBytes(Path.Combine(dir, "war3map.wpm")).SequenceEqual(originalWpm);
        return (changed, undo);
    }

    private static (bool Claimed, bool Roundtrip, bool Removal) RunTierBMatrix(
        string source,
        string work,
        string manifestPath)
    {
        var ownership = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["part.sounds"] = new[] { "war3map.w3s" },
            ["part.imports"] = new[] { "war3map.imp" },
            ["part.gameplay"] = new[] { "war3mapMisc.txt", "war3mapSkin.txt" },
            ["part.triggerstrings"] = new[] { "war3map.wts" },
            ["part.triggers"] = new[] { "war3map.wtg", "war3map.wct", "war3map.j", "war3map.lua" },
            ["part.objects"] = new[]
            {
                "war3map.w3u", "war3map.w3t", "war3map.w3a", "war3map.w3b",
                "war3map.w3d", "war3map.w3q", "war3map.w3h",
                "war3mapSkin.w3u", "war3mapSkin.w3t", "war3mapSkin.w3a",
                "war3mapSkin.w3b", "war3mapSkin.w3d", "war3mapSkin.w3q",
                "war3mapSkin.w3h",
            },
        };

        string matrixDir = Path.GetFullPath(Path.Combine(work, "..", "module_host_accept_tier_b"));
        if (Directory.Exists(matrixDir))
            Directory.Delete(matrixDir, recursive: true);
        Directory.CreateDirectory(matrixDir);
        CopyFolder(source, matrixDir);

        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int sequence = 1;
        foreach (string file in ownership.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = BuildTierBFixture(file, sequence++);
            File.WriteAllBytes(Path.Combine(matrixDir, file), bytes);
            expected[file] = bytes;
        }

        bool claimed;
        bool roundtrip;
        using (var tierBHost = new ModuleHost())
        {
            tierBHost.Load(manifestPath);
            MapDocument tierBDoc = tierBHost.OpenDocument(matrixDir, MapFormatKind.War3Folder);
            claimed = ownership.Keys.All(id =>
                tierBDoc.GetPart(id["part.".Length..]) != null);
            tierBDoc.Save();
            roundtrip = expected.All(pair =>
                File.ReadAllBytes(Path.Combine(matrixDir, pair.Key)).SequenceEqual(pair.Value));
        }

        bool removal = true;
        foreach ((string moduleId, string[] files) in ownership)
        {
            string caseDir = Path.Combine(matrixDir, "..", "tier_b_drop_" + moduleId.Replace('.', '_'));
            caseDir = Path.GetFullPath(caseDir);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, recursive: true);
            Directory.CreateDirectory(caseDir);
            CopyFolder(source, caseDir);
            foreach (string file in files)
                File.WriteAllBytes(Path.Combine(caseDir, file), expected[file]);

            ModulesFile reduced = ModuleHost.ReadManifest(manifestPath);
            reduced.Modules.First(m => m.Id == moduleId).Enabled = false;
            using var dropHost = new ModuleHost();
            dropHost.Load(reduced);
            MapDocument dropDoc = dropHost.OpenDocument(caseDir, MapFormatKind.War3Folder);
            bool unknown = files.All(file => dropDoc.UnknownParts.Any(part =>
                part.FileName.Equals(file, StringComparison.OrdinalIgnoreCase)));
            dropDoc.Save();
            bool intact = files.All(file =>
                File.ReadAllBytes(Path.Combine(caseDir, file)).SequenceEqual(expected[file]));
            removal &= unknown && intact;
        }

        return (claimed, roundtrip, removal);
    }

    private static bool Is(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) && v is true;

    /// <summary>
    /// Minimal context for tools invoked outside a shell. Capability lookups
    /// return null, which is exactly the degradation path modules must handle.
    /// </summary>
    private static IModuleContext FakeContext(ModuleHost host) => new HeadlessContext(host);

    private sealed class HeadlessContext(ModuleHost host) : IModuleContext
    {
        public IMapDocument? Document => host.Document;
        public ICapabilities Capabilities => host.Capabilities;
        public IEditorLog Log => host.Log;
        public string ModuleId => "accept.harness";
        public event Action<IMapDocument?>? DocumentChanged { add { } remove { } }
        public void AddCommand(CommandDescriptor command) { }
        public void AddTool(IToolDescriptor tool) { }
        public void AddPanel(IPanelDescriptor panel) { }
        public void AddWindow(IWindowDescriptor window) { }
        public void AddOverlay(IViewportOverlay overlay) { }
        public void AddPartCodec(IPartCodec codec) { }
        public void AddAssetSource(IAssetSource source) { }
        public void ProvideCapability<T>(T instance) where T : class { }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(from, file).Replace('\\', '/');
            if (AtomicFileSet.IsKernelFile(name))
                continue;
            string target = Path.Combine(to, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
