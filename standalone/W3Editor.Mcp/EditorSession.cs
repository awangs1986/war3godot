using System.Diagnostics;
using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// In-process headless editor session shared by all MCP tools in one Cursor connection.
/// </summary>
public sealed class EditorSession
{
    public MapSession Map { get; } = new();
    public EditHistory History { get; } = new();
    public EditorAssets Assets { get; } = new();
    public IObjectCatalog Catalog { get; }

    public EditorSession()
    {
        Catalog = new ObjectCatalog(Assets);
    }

    public void RequireOpen()
    {
        if (!Map.IsOpen || Map.Map == null)
            throw new InvalidOperationException("No map open. Call map_open or map_new first.");
    }

    public string StatusJson()
    {
        var map = Map.Map;
        if (map == null)
            return JsonSerializer.Serialize(new { open = false });
        return JsonSerializer.Serialize(new
        {
            open = true,
            source = Map.SourcePath,
            w3e = Map.W3ePath,
            folder = Map.IsFolder,
            dirty = Map.IsDirty,
            tileset = map.Tileset.ToString(),
            tilesetName = Map.TilesetDisplayName,
            tiles = new { w = map.TileCountX, h = map.TileCountY },
            corners = new { w = map.Width, h = map.Height },
            groundTiles = map.GroundTilesets,
            cliffTiles = map.CliffTilesets,
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
            canUndo = History.CanUndo,
            canRedo = History.CanRedo,
            gameData = Assets.HasGameData,
            catalog = new { doodads = Catalog.Doodads.Count, units = Catalog.Units.Count },
        });
    }

    public string Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required");
        path = Path.GetFullPath(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".w3x" or ".w3m")
        {
            var (folder, files) = W3xImporter.Extract(path);
            Map.Open(folder);
            History.Clear();
            SyncAssetsMapDir();
            return $"Unpacked {files} files → {folder}\n{StatusJson()}";
        }
        Map.Open(path);
        History.Clear();
        SyncAssetsMapDir();
        return StatusJson();
    }

    public string CreateNew(string name, int widthTiles, int heightTiles, char tileset, string? initialTileId)
    {
        if (!W3EMap.IsValidNewSize(widthTiles) || !W3EMap.IsValidNewSize(heightTiles))
            throw new ArgumentException(
                $"Invalid size {widthTiles}x{heightTiles}: each side must be {W3EMap.MinTiles}–{W3EMap.MaxTiles} and a multiple of {W3EMap.TileStep}.");
        var settings = BuildNewMapSettings(tileset, initialTileId);
        string folder = Map.CreateNew(name, widthTiles, heightTiles, settings);
        History.Clear();
        SyncAssetsMapDir();
        return $"Created {folder}\n{StatusJson()}";
    }

    public NewMapSettings BuildNewMapSettings(char tileset, string? initialTileId)
    {
        tileset = char.ToUpperInvariant(tileset);
        if (!Assets.HasGameData)
            return NewMapSettings.LordaeronSummer;
        var grounds = Assets.GroundTilesFor(tileset);
        var cliffs = Assets.CliffTypesFor(tileset);
        if (grounds.Count == 0 || cliffs.Count == 0)
            return NewMapSettings.LordaeronSummer;
        int initial = 0;
        if (!string.IsNullOrWhiteSpace(initialTileId))
        {
            int idx = grounds.FindIndex(t => t.Id.Equals(initialTileId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new ArgumentException($"Unknown ground tile '{initialTileId}' for tileset {tileset}.");
            initial = idx;
        }
        else
        {
            int grass = grounds.FindIndex(t => t.Comment.Contains("Grass", StringComparison.OrdinalIgnoreCase));
            initial = Math.Max(0, grass);
        }
        return new NewMapSettings
        {
            Tileset = tileset,
            GroundTilesets = grounds.Select(t => t.Id).ToList(),
            CliffTilesets = cliffs.Select(c => c.Id).ToList(),
            InitialTextureIndex = initial,
        };
    }

    public void Save()
    {
        RequireOpen();
        Map.Save();
    }

    public void SyncAssetsMapDir()
    {
        Assets.MapDir = Map.IsFolder ? Map.SourcePath : "";
    }

    /// <summary>World Z (W3 up) at W3 world X/Y — bilinear sample of corner final heights.</summary>
    public float SampleGroundZ(float worldX, float worldY)
    {
        var map = Map.Map!;
        float cx = (worldX - map.CenterOffsetX) / W3EMap.TileSize;
        float cy = (worldY - map.CenterOffsetY) / W3EMap.TileSize;
        int x0 = (int)MathF.Floor(cx), y0 = (int)MathF.Floor(cy);
        float fx = cx - x0, fy = cy - y0;
        float h00 = CornerWorldY(map, x0, y0);
        float h10 = CornerWorldY(map, x0 + 1, y0);
        float h01 = CornerWorldY(map, x0, y0 + 1);
        float h11 = CornerWorldY(map, x0 + 1, y0 + 1);
        return (h00 * (1 - fx) + h10 * fx) * (1 - fy) + (h01 * (1 - fx) + h11 * fx) * fy;
    }

    private static float CornerWorldY(W3EMap map, int x, int y)
    {
        x = Math.Clamp(x, 0, map.Width - 1);
        y = Math.Clamp(y, 0, map.Height - 1);
        return map.Corners[y * map.Width + x].WorldY();
    }

    public string BrushStamp(
        string opName, int cx, int cy, int? toCx, int? toCy,
        int radius, float strength, int textureIndex, bool erase)
    {
        RequireOpen();
        var map = Map.Map!;
        if (!Enum.TryParse<BrushOp>(opName, ignoreCase: true, out var op) || op == BrushOp.None || op == BrushOp.WaterErase)
            throw new ArgumentException($"Unknown brush op '{opName}'. Use Texture/Raise/Lower/Plateau/Smooth/Cliff*/Water*/Ramp.");
        var settings = new BrushSettings
        {
            Op = op,
            Radius = Math.Clamp(radius, 1, 32),
            Strength = Math.Clamp(strength, 0f, 1f),
            TextureIndex = Math.Clamp(textureIndex, 0, Math.Max(0, map.GroundTilesets.Count - 1)),
        };
        var stroke = new BrushStroke();
        History.BeginStroke(map);
        int x1 = toCx ?? cx, y1 = toCy ?? cy;
        int steps = Math.Max(1, Math.Max(Math.Abs(x1 - cx), Math.Abs(y1 - cy)));
        // HiveWE: first apply(0.5); subsequent use strength as frame_delta stand-in
        float firstDelta = 0.5f;
        float dragDelta = Math.Clamp(strength, 0.01f, 1f);
        for (int i = 0; i <= steps; i++)
        {
            int ix = cx + (x1 - cx) * i / steps;
            int iy = cy + (y1 - cy) * i / steps;
            ix = Math.Clamp(ix, 0, map.Width - 1);
            iy = Math.Clamp(iy, 0, map.Height - 1);
            TerrainBrush.Apply(map, ix, iy, settings, erase, stroke,
                frameDelta: i == 0 ? firstDelta : dragDelta);
        }
        History.CommitStroke(map);
        Map.MarkDirty();
        return JsonSerializer.Serialize(new
        {
            op = op.ToString(),
            from = new { x = cx, y = cy },
            to = new { x = x1, y = y1 },
            stamps = steps + 1,
            dirty = true,
            canUndo = History.CanUndo,
        });
    }

    public string PlaceObject(
        string id, float x, float y, bool isUnit,
        int? variation, float? angleDeg, float? scale, int owner)
    {
        RequireOpen();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 4)
            throw new ArgumentException("id must be a 4-character rawcode");
        id = id.PadRight(4)[..4];
        float scaleV = scale is > 0.0001f ? scale.Value : 1f;
        float angle = (angleDeg ?? (isUnit ? 270f : 0f)) * MathF.PI / 180f;
        int varIndex = variation ?? 0;
        if (!isUnit)
        {
            var info = Catalog.FindDoodad(id);
            if (info != null && variation == null)
                varIndex = 0;
        }
        var p = new W3Placement
        {
            Id = id,
            IsUnit = isUnit,
            Variation = Math.Max(0, varIndex),
            X = x,
            Y = y,
            Z = SampleGroundZ(x, y),
            Angle = angle,
            ScaleX = scaleV,
            ScaleY = scaleV,
            ScaleZ = scaleV,
            PlayerOwner = Math.Clamp(owner, 0, 15),
            Life = isUnit ? -1 : 100,
            CreationNumber = Map.NextCreationNumber(),
            SkinId = id,
            Flags = 2,
        };
        Map.ListFor(p).Add(p);
        History.Push(new AddPlacementsCommand(new[] { p }));
        Map.MarkDirty();
        return JsonSerializer.Serialize(new
        {
            id = p.Id,
            isUnit = p.IsUnit,
            creationNumber = p.CreationNumber,
            x = p.X,
            y = p.Y,
            z = p.Z,
            angleDeg = p.Angle * 180f / MathF.PI,
            variation = p.Variation,
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
        });
    }

    public string RemoveObjects(int? creationNumber, float? x, float? y, float radius, string kind = "any")
    {
        RequireOpen();
        // Creation numbers are per-file sequences (war3map.doo vs
        // war3mapUnits.doo both count from 0), so a bare number is ambiguous —
        // callers must scope by kind to avoid deleting a same-numbered
        // placement from the other file.
        IEnumerable<W3Placement> pool = kind switch
        {
            "doodad" or "doodads" => Map.Doodads,
            "unit" or "units" => Map.Units,
            "any" or "" => Map.Doodads.Concat(Map.Units),
            _ => throw new ArgumentException($"kind must be doodad|unit|any, got '{kind}'"),
        };
        List<W3Placement> victims;
        if (creationNumber is int cn)
        {
            if (kind is "any" or "")
                throw new ArgumentException(
                    "creationNumber collides across doodads/units (per-file sequences); pass kind=doodad or kind=unit");
            victims = pool.Where(p => p.CreationNumber == cn).ToList();
        }
        else if (x is float wx && y is float wy)
        {
            float r2 = radius * radius;
            victims = pool
                .Where(p =>
                {
                    float dx = p.X - wx, dy = p.Y - wy;
                    return dx * dx + dy * dy <= r2;
                })
                .ToList();
        }
        else
        {
            throw new ArgumentException("Provide creationNumber, or x+y (+ optional radius).");
        }
        if (victims.Count == 0)
            return JsonSerializer.Serialize(new { removed = 0 });
        History.Push(RemovePlacementsCommand.Execute(Map, victims));
        Map.MarkDirty();
        return JsonSerializer.Serialize(new
        {
            removed = victims.Count,
            ids = victims.Select(p => new { p.Id, p.CreationNumber, p.IsUnit }).ToList(),
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
        });
    }

    public string CatalogSearch(string? query, string kind, char? category, int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        query = query?.Trim() ?? "";
        kind = (kind ?? "all").ToLowerInvariant();
        var results = new List<object>();
        if (kind is "all" or "doodad" or "doodads")
        {
            foreach (var d in Catalog.Doodads)
            {
                if (category is char cat && cat != '\0' && d.Category != cat) continue;
                if (query.Length > 0
                    && !d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !d.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(new
                {
                    kind = d.IsDestructible ? "destructible" : "doodad",
                    d.Id,
                    d.Name,
                    category = d.Category.ToString(),
                    d.NumVar,
                    d.DefScale,
                    d.Tilesets,
                });
                if (results.Count >= limit) break;
            }
        }
        if (results.Count < limit && kind is "all" or "unit" or "units")
        {
            foreach (var u in Catalog.Units)
            {
                if (query.Length > 0
                    && !u.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !u.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(new { kind = "unit", u.Id, u.Name, u.ModelScale });
                if (results.Count >= limit) break;
            }
        }
        return JsonSerializer.Serialize(new { count = results.Count, items = results });
    }

    public string ListPlacements(string kind, string? idFilter, int limit)
    {
        RequireOpen();
        limit = Math.Clamp(limit, 1, 500);
        kind = (kind ?? "all").ToLowerInvariant();
        IEnumerable<W3Placement> src = kind switch
        {
            "doodad" or "doodads" => Map.Doodads,
            "unit" or "units" => Map.Units,
            _ => Map.Doodads.Concat(Map.Units),
        };
        if (!string.IsNullOrWhiteSpace(idFilter))
            src = src.Where(p => p.Id.Contains(idFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        var list = src.Take(limit).Select(p => new
        {
            p.Id,
            p.IsUnit,
            p.CreationNumber,
            p.X,
            p.Y,
            p.Z,
            angleDeg = p.Angle * 180f / MathF.PI,
            p.Variation,
            p.PlayerOwner,
        }).ToList();
        var groups = (kind is "doodad" or "doodads" ? Map.Doodads : kind is "unit" or "units" ? Map.Units : Map.Doodads.Concat(Map.Units))
            .GroupBy(p => p.Id)
            .OrderByDescending(g => g.Count())
            .Take(40)
            .Select(g => new { id = g.Key, count = g.Count() })
            .ToList();
        return JsonSerializer.Serialize(new
        {
            listed = list.Count,
            totalDoodads = Map.Doodads.Count,
            totalUnits = Map.Units.Count,
            topTypes = groups,
            items = list,
        });
    }

    public string Undo()
    {
        RequireOpen();
        if (!History.Undo(Map))
            return JsonSerializer.Serialize(new { ok = false, reason = "nothing to undo" });
        Map.MarkDirty();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
            canUndo = History.CanUndo,
            canRedo = History.CanRedo,
            touchedPlacements = History.LastTouchedPlacements,
        });
    }

    public string Redo()
    {
        RequireOpen();
        if (!History.Redo(Map))
            return JsonSerializer.Serialize(new { ok = false, reason = "nothing to redo" });
        Map.MarkDirty();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
            canUndo = History.CanUndo,
            canRedo = History.CanRedo,
            touchedPlacements = History.LastTouchedPlacements,
        });
    }

    /// <summary>Save then spawn W3Editor.exe --open --shot3d. Returns absolute png path.</summary>
    public async Task<string> Screenshot3dAsync(string? outputPath, CancellationToken ct)
    {
        RequireOpen();
        if (!Map.IsFolder)
            throw new InvalidOperationException("screenshot_3d requires a map folder (not a bare .w3e).");
        if (Map.IsDirty)
            Map.Save();

        string outDir = EditorPaths.OutDir;
        Directory.CreateDirectory(outDir);
        string png = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(outDir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png")
            : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);

        string exe = FindW3EditorExe()
            ?? throw new FileNotFoundException("W3Editor.exe not found. Build standalone/W3Editor first (Debug or Release).");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.ArgumentList.Add("--open");
        psi.ArgumentList.Add(Map.SourcePath);
        psi.ArgumentList.Add("--shot3d");
        psi.ArgumentList.Add(png);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start W3Editor.exe");
        await proc.WaitForExitAsync(ct);
        if (!File.Exists(png))
            throw new InvalidOperationException($"Screenshot missing after exit={proc.ExitCode}. Log: {Path.ChangeExtension(png, ".txt")}");
        return png;
    }

    public static string? FindW3EditorExe()
    {
        var root = GodotLauncher.FindProjectRoot();
        var candidates = new List<string>();
        if (root != null)
        {
            candidates.Add(Path.Combine(root, "standalone", "W3Editor", "bin", "Release", "net8.0", "W3Editor.exe"));
            candidates.Add(Path.Combine(root, "standalone", "W3Editor", "bin", "Debug", "net8.0", "W3Editor.exe"));
        }
        // Sibling of this MCP binary when both are built under standalone/
        string baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "W3Editor", "bin", "Release", "net8.0", "W3Editor.exe")));
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "W3Editor", "bin", "Debug", "net8.0", "W3Editor.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Read-only structural dump of the open map (for rebuild reference).</summary>
    public string AnalyzeJson(int coarseStep = 8)
    {
        RequireOpen();
        var map = Map.Map!;
        coarseStep = Math.Clamp(coarseStep, 2, 32);
        var layers = new int[16];
        var tex = new int[Math.Max(1, map.GroundTilesets.Count)];
        int water = 0, ramp = 0, blight = 0, edge = 0;
        float gmin = float.MaxValue, gmax = float.MinValue;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            W3Corner c = map.Corners[y * map.Width + x];
            layers[Math.Clamp(c.LayerHeight, 0, 15)]++;
            if (c.GroundTexture >= 0 && c.GroundTexture < tex.Length) tex[c.GroundTexture]++;
            if (c.Water) water++;
            if (c.Ramp) ramp++;
            if (c.Blight) blight++;
            if (c.MapEdge || c.Boundary) edge++;
            gmin = Math.Min(gmin, c.GroundHeight);
            gmax = Math.Max(gmax, c.GroundHeight);
        }
        var coarse = new List<object>();
        for (int y = 0; y < map.Height; y += coarseStep)
        for (int x = 0; x < map.Width; x += coarseStep)
        {
            W3Corner c = map.Corners[y * map.Width + x];
            coarse.Add(new
            {
                x, y, l = c.LayerHeight, t = c.GroundTexture,
                w = c.Water ? 1 : 0, r = c.Ramp ? 1 : 0,
                gh = Math.Round(c.GroundHeight, 2),
            });
        }
        var doodadTop = Map.Doodads.GroupBy(p => p.Id).OrderByDescending(g => g.Count()).Take(30)
            .Select(g => new { id = g.Key, n = g.Count(), name = Catalog.FindDoodad(g.Key)?.Name }).ToList();
        var unitTop = Map.Units.GroupBy(p => p.Id).OrderByDescending(g => g.Count()).Take(25)
            .Select(g => new { id = g.Key, n = g.Count(), name = Catalog.FindUnit(g.Key)?.Name }).ToList();
        var startLocs = Map.Units.Where(p => p.IsStartLocation)
            .Select(p => new { p.X, p.Y, p.PlayerOwner, p.CreationNumber }).ToList();
        return JsonSerializer.Serialize(new
        {
            source = Map.SourcePath,
            tileset = map.Tileset.ToString(),
            tilesetName = Map.TilesetDisplayName,
            version = map.Version,
            tiles = new { w = map.TileCountX, h = map.TileCountY },
            corners = new { w = map.Width, h = map.Height },
            offset = new { x = map.CenterOffsetX, y = map.CenterOffsetY },
            ground = map.GroundTilesets,
            cliffs = map.CliffTilesets,
            waterCorners = water,
            rampCorners = ramp,
            blightCorners = blight,
            edgeCorners = edge,
            groundHeightRange = new { min = gmin, max = gmax },
            layerHist = layers.Select((n, i) => new { layer = i, n }).Where(x => x.n > 0),
            texHist = Enumerable.Range(0, map.GroundTilesets.Count)
                .Select(i => new { i, id = map.GroundTilesets[i], n = tex[i] })
                .OrderByDescending(x => x.n),
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
            startLocations = startLocs,
            doodadTop,
            unitTop,
            coarseGridStep = coarseStep,
            coarseGrid = coarse,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Draft-rebuild Lost Temple into a NEW map folder.
    /// Terrain corners are read from the reference (allowed info path);
    /// doodads/units are placed through the same PlaceObject path MCP tools use.
    /// Optional brush polish passes keep the MCP brush pipeline exercised.
    /// </summary>
    public string RebuildLostTempleDraft(string referencePath, string newMapName = "LostTemple_MCP", bool brushPolish = true)
    {
        var refSession = new EditorSession();
        refSession.Open(referencePath);
        var src = refSession.Map.Map
            ?? throw new InvalidOperationException("reference has no w3e");

        int grassIdx = src.GroundTilesets.FindIndex(id => id.Equals("Lgrs", StringComparison.OrdinalIgnoreCase));
        if (grassIdx < 0) grassIdx = 0;
        CreateNew(newMapName, src.TileCountX, src.TileCountY, src.Tileset, src.GroundTilesets[grassIdx]);

        var dst = Map.Map!;
        dst.Tileset = src.Tileset;
        dst.GroundTilesets = src.GroundTilesets.ToList();
        dst.CliffTilesets = src.CliffTilesets.ToList();
        dst.CenterOffsetX = src.CenterOffsetX;
        dst.CenterOffsetY = src.CenterOffsetY;
        dst.Version = src.Version;

        // Corner-faithful terrain from reference (read path — not a silent .w3e file copy).
        int n = Math.Min(src.Corners.Length, dst.Corners.Length);
        for (int i = 0; i < n; i++)
        {
            W3Corner a = src.Corners[i], b = dst.Corners[i];
            b.GroundHeight = a.GroundHeight;
            b.WaterHeight = a.WaterHeight;
            b.MapEdge = a.MapEdge;
            b.Ramp = a.Ramp;
            b.Blight = a.Blight;
            b.Water = a.Water;
            b.Boundary = a.Boundary;
            b.GroundTexture = a.GroundTexture;
            b.CliffVariation = a.CliffVariation;
            b.GroundVariation = a.GroundVariation;
            b.CliffTexture = a.CliffTexture;
            b.LayerHeight = a.LayerHeight;
        }
        History.Clear();
        Map.MarkDirty();

        int stamps = 0;
        if (brushPolish)
        {
            // Light MCP-brush pass: re-stamp a few stone bands + one water pocket so the
            // brush/undo stack is warm for subsequent agent edits.
            int stone = src.GroundTilesets.FindIndex(id => id.Equals("Ysqd", StringComparison.OrdinalIgnoreCase));
            if (stone < 0) stone = Math.Min(3, src.GroundTilesets.Count - 1);
            int mx = dst.Width / 2, my = dst.Height / 2;
            BrushStamp("Texture", mx - 20, my, mx + 20, my, radius: 5, strength: 1f, textureIndex: stone, erase: false);
            stamps++;
            BrushStamp("Texture", mx, my - 20, mx, my + 20, radius: 5, strength: 1f, textureIndex: stone, erase: false);
            stamps++;
        }

        int placed = 0;
        // Trees / destructibles: keep every 2nd to stay editable; landmarks denser.
        int iTree = 0;
        foreach (W3Placement p in refSession.Map.Doodads)
        {
            var info = Catalog.FindDoodad(p.Id);
            char cat = info?.Category ?? '?';
            bool keep = cat is 'D' or 'E' or 'O' or 'S' or 'Z';
            if (!keep) continue;
            if (cat == 'D' && (iTree++ % 2) != 0) continue;
            if (cat != 'D' && (iTree++ % 5) != 0) continue;
            PlaceObject(p.Id, p.X, p.Y, false, p.Variation, p.Angle * 180f / MathF.PI,
                MathF.Abs(p.ScaleX) > 0.01f ? p.ScaleX : 1f, 0);
            placed++;
            if (placed >= 2200) break;
        }

        var landmarkIds = new HashSet<string>(StringComparer.Ordinal)
            { "sloc", "ngol", "ngme", "nfoh", "nmoo", "ntav", "ngol" };
        var seen = new HashSet<(int, int)>();
        foreach (W3Placement u in refSession.Map.Units)
        {
            if (!u.IsStartLocation && !landmarkIds.Contains(u.Id)) continue;
            var key = ((int)MathF.Round(u.X), (int)MathF.Round(u.Y));
            if (!seen.Add(key)) continue;
            PlaceObject(u.Id, u.X, u.Y, true, u.Variation, u.Angle * 180f / MathF.PI, null, u.PlayerOwner);
            placed++;
        }

        Save();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            folder = Map.SourcePath,
            reference = referencePath,
            terrain = "corner-sync from reference",
            brushPolishStamps = stamps,
            placementsAdded = placed,
            doodads = Map.Doodads.Count,
            units = Map.Units.Count,
            analyzeHint = "re-open with --analyze for stats",
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
