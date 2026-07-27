using System.Diagnostics;
using System.Text;
using Diablo1Like.MapFormat;
using W3Editor.Formats;

namespace W3Editor.Services;

public enum MapFormatKind
{
    None,
    War3Folder,
    War3EnvironmentFile,
    D1MapFolder,
}

/// <summary>Currently opened map: source paths + parsed w3e/doo/units/wpm/w3i.</summary>
public sealed class MapSession
{
    public string SourcePath { get; private set; } = "";
    public string W3ePath { get; private set; } = "";
    public W3EMap? Map { get; private set; }
    /// <summary>Parsed doo documents (raw-record fidelity); placements live inside.</summary>
    public W3DooDocument DoodadsDoc { get; private set; } = new() { IsUnits = false };
    public W3DooDocument UnitsDoc { get; private set; } = new() { IsUnits = true };
    public List<W3Placement> Doodads => DoodadsDoc.Placements;
    public List<W3Placement> Units => UnitsDoc.Placements;
    public W3PathingMap? Pathing { get; private set; }
    public W3RegionsFile? Regions { get; private set; }
    public W3MapInfoVersion? Info { get; private set; }
    public W3MapInfoIdentity? MapIdentity { get; private set; }
    /// <summary>Unsaved edits (w3e or placements).</summary>
    public bool IsDirty { get; private set; }
    public MapFormatKind FormatKind { get; private set; }
    public bool IsNativeFormat => FormatKind == MapFormatKind.D1MapFolder;
    private NativeMap? _nativeTemplate;
    private bool _pathingDirty;
    private bool _regionsDirty;
    private bool _mapIdentityDirty;

    public List<W3Placement> ListFor(W3Placement p) => p.IsUnit ? Units : Doodads;

    public bool IsOpen => Map != null;
    /// <summary>True when SourcePath is an extracted map folder (doo/wpm alongside w3e).</summary>
    public bool IsFolder => FormatKind is MapFormatKind.War3Folder or MapFormatKind.D1MapFolder;

    private static readonly Dictionary<char, string> TilesetNames = new()
    {
        ['A'] = "灰谷", ['B'] = "贫瘠之地", ['C'] = "费尔伍德", ['D'] = "地下城",
        ['F'] = "洛丹伦(秋)", ['G'] = "地底", ['I'] = "冰封王座", ['J'] = "达拉然废墟",
        ['K'] = "黑色城堡", ['L'] = "洛丹伦(夏)", ['N'] = "诺森德", ['O'] = "边缘之地",
        ['Q'] = "村庄(秋)", ['V'] = "村庄", ['W'] = "洛丹伦(冬)", ['X'] = "达拉然",
        ['Y'] = "城市", ['Z'] = "沉沦的废墟",
    };

    public string TilesetDisplayName =>
        Map == null ? "-" :
        TilesetNames.TryGetValue(Map.Tileset, out var name) ? $"{name} ({Map.Tileset})" : Map.Tileset.ToString();

    public static string TilesetName(char letter) =>
        TilesetNames.TryGetValue(letter, out var name) ? name : letter.ToString();

    /// <summary>All WE tileset letters in menu order.</summary>
    public static IReadOnlyList<char> TilesetLetters { get; } =
        new[] { 'L', 'F', 'W', 'B', 'A', 'C', 'N', 'V', 'Q', 'D', 'G', 'Y', 'X', 'J', 'Z', 'I', 'O', 'K' };

    /// <summary>
    /// Creates a blank HiveWE-style map folder (war3map.w3e + empty doo files)
    /// and opens it. Returns the created folder.
    /// </summary>
    public string CreateNew(string name, int widthTiles, int heightTiles, NewMapSettings? settings = null)
    {
        var folder = NewMapFolder(name);
        Directory.CreateDirectory(folder);
        var map = W3EMap.CreateBlank(widthTiles, heightTiles, settings);
        File.WriteAllBytes(Path.Combine(folder, "war3map.w3e"), map.SaveBytes());
        // Empty placement files (HiveWE Doodads::save_empty / Units::save_empty).
        // No war3map.w3i is written, so skin-id fourccs stay off (classic layout).
        File.WriteAllBytes(Path.Combine(folder, "war3map.doo"),
            W3DooDocument.CreateBlankDoodads(reforged: false).Write());
        File.WriteAllBytes(Path.Combine(folder, "war3mapUnits.doo"),
            W3DooDocument.CreateBlankUnits(reforged: false).Write());
        Open(folder);
        return folder;
    }

    /// <summary>&lt;repo&gt;/war3/maps/&lt;name&gt;/source, suffixed when taken (same layout as .w3x import).</summary>
    private static string NewMapFolder(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        if (string.IsNullOrWhiteSpace(name))
            name = "新建地图";
        var root = GodotLauncher.FindProjectRoot();
        string Base(string n) => root != null
            ? Path.Combine(root, "war3", "maps", n, "source")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "W3Maps", n);
        var folder = Base(name);
        for (int i = 2; Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(); i++)
            folder = Base($"{name} ({i})");
        return folder;
    }

    /// <summary>
    /// Open an extracted map folder (containing war3map.w3e) or a bare .w3e file.
    /// MPQ archives are intentionally unsupported (Reforged-only pipeline).
    /// </summary>
    public void Open(string path)
    {
        string nativeFolder = File.Exists(path)
            && Path.GetFileName(path).Equals(
                NativeMapFormat.ManifestFile, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(path))!
            : path;
        if (Directory.Exists(nativeFolder)
            && NativeMapFormat.IsNativeMapFolder(nativeFolder))
        {
            NativeMap native = NativeMapFormat.Load(nativeFolder);
            NativeMapBridge.EditorModel editor = NativeMapBridge.ToEditorModel(native);
            Map = editor.Terrain;
            DoodadsDoc = editor.Doodads;
            UnitsDoc = editor.Units;
            SourcePath = Path.GetFullPath(nativeFolder);
            W3ePath = "";
            Pathing = null;
            Regions = null;
            Info = null;
            MapIdentity = null;
            FormatKind = MapFormatKind.D1MapFolder;
            _nativeTemplate = native;
            IsDirty = false;
            return;
        }

        string w3e;
        string? folder = null;
        if (Directory.Exists(path))
        {
            folder = path;
            w3e = Path.Combine(path, "war3map.w3e");
            if (!File.Exists(w3e))
                throw new FileNotFoundException($"目录中找不到 war3map.w3e：{path}");
        }
        else if (File.Exists(path) && Path.GetExtension(path).Equals(".w3e", StringComparison.OrdinalIgnoreCase))
        {
            w3e = path;
        }
        else
        {
            throw new InvalidDataException(
                "仅支持已解压的地图目录或 .w3e 文件。MPQ (.w3x/.w3m) 请先离线解包（见 war3/README.md）。");
        }

        Map = W3EMap.Load(w3e);
        SourcePath = path;
        W3ePath = w3e;
        IsDirty = false;
        FormatKind = folder == null
            ? MapFormatKind.War3EnvironmentFile
            : MapFormatKind.War3Folder;
        _nativeTemplate = null;
        DoodadsDoc = new W3DooDocument { IsUnits = false };
        UnitsDoc = new W3DooDocument { IsUnits = true };
        Pathing = null;
        Regions = null;
        Info = null;
        MapIdentity = null;
        _pathingDirty = false;
        _regionsDirty = false;
        _mapIdentityDirty = false;

        if (folder == null)
            return;

        // Sidecar files are best-effort: a damaged doo must not block the map.
        Info = TryRead(folder, "war3map.w3i", W3MapInfoVersion.Parse);
        MapIdentity = TryRead(folder, "war3map.w3i", W3MapInfoIdentity.Parse);
        bool hasSkins = Info?.HasReforgedSkinIds ?? false;
        DoodadsDoc = TryRead(folder, "war3map.doo", b => W3DooFile.ParseDoodadsDocument(b, hasSkins))
            ?? new W3DooDocument { IsUnits = false, HasSkinIds = hasSkins };
        UnitsDoc = TryRead(folder, "war3mapUnits.doo", b => W3DooFile.ParseUnitsDocument(b, hasSkins))
            ?? new W3DooDocument { IsUnits = true, HasSkinIds = hasSkins };
        Pathing = TryRead(folder, "war3map.wpm", W3PathingMap.LoadBytes);
        Regions = TryRead(folder, "war3map.w3r", W3RegionsFile.LoadBytes);
    }

    private static T? TryRead<T>(string folder, string file, Func<byte[], T> parse) where T : class
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) return null;
        try
        {
            return parse(File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MapSession: {file} 解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes w3e (always) and, for folder maps, war3map.doo / war3mapUnits.doo
    /// back to disk. Each file keeps a .bak of its previous on-disk version.
    /// </summary>
    public void Save()
    {
        if (Map == null)
            throw new InvalidOperationException("未打开地图");
        if (IsNativeFormat)
        {
            NativeMap native = CaptureNative(
                _nativeTemplate?.Name ?? Path.GetFileName(SourcePath));
            NativeMapFormat.Save(native, SourcePath);
            _nativeTemplate = native;
            IsDirty = false;
            return;
        }
        WriteWithBackup(W3ePath, Map.SaveBytes());
        if (IsFolder)
        {
            WriteWithBackup(Path.Combine(SourcePath, "war3map.doo"), DoodadsDoc.Write());
            WriteWithBackup(Path.Combine(SourcePath, "war3mapUnits.doo"), UnitsDoc.Write());
            if (_pathingDirty && Pathing != null)
                WriteWithBackup(Path.Combine(SourcePath, "war3map.wpm"), Pathing.SaveBytes());
            if (_regionsDirty && Regions != null)
                WriteWithBackup(Path.Combine(SourcePath, "war3map.w3r"), Regions.SaveBytes());
            if (_mapIdentityDirty && MapIdentity != null)
                WriteWithBackup(Path.Combine(SourcePath, "war3map.w3i"), MapIdentity.Write());
        }
        _pathingDirty = false;
        _regionsDirty = false;
        _mapIdentityDirty = false;
        IsDirty = false;
    }

    /// <summary>
    /// GUI/module save path. Session-owned live models replace duplicate
    /// module snapshots, but the document performs the only disk commit.
    /// </summary>
    public void Save(W3Editor.Contracts.IMapDocument document)
    {
        if (Map == null)
            throw new InvalidOperationException("No map is open.");
        ArgumentNullException.ThrowIfNull(document);
        string expectedFolder = IsNativeFormat || IsFolder
            ? Path.GetFullPath(SourcePath)
            : Path.GetDirectoryName(Path.GetFullPath(W3ePath))!;
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(document.Folder)),
                Path.TrimEndingDirectorySeparator(expectedFolder),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Module document and MapSession refer to different map folders.");

        if (document is not W3Editor.Contracts.IAuthoritativeMapDocument atomicDocument)
            throw new NotSupportedException(
                "The active document host cannot merge live session payloads atomically.");
        atomicDocument.Save(CaptureSavePayloads());
        if (IsNativeFormat)
            _nativeTemplate = CaptureNative(
                _nativeTemplate?.Name ?? Path.GetFileName(SourcePath));
        _pathingDirty = false;
        _regionsDirty = false;
        _mapIdentityDirty = false;
        IsDirty = false;
    }

    /// <summary>Serializes all current session-owned map files without disk I/O.</summary>
    public IReadOnlyDictionary<string, byte[]> CaptureSavePayloads()
    {
        if (Map == null)
            throw new InvalidOperationException("No map is open.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (IsNativeFormat)
        {
            NativeMap native = CaptureNative(
                _nativeTemplate?.Name ?? Path.GetFileName(SourcePath));
            foreach ((string name, byte[] data) in NativeMapFormat.SerializeFiles(native))
                files[name] = data;
            return files;
        }

        files[IsFolder ? "war3map.w3e" : Path.GetFileName(W3ePath)] = Map.SaveBytes();
        if (!IsFolder)
            return files;
        files["war3map.doo"] = DoodadsDoc.Write();
        files["war3mapUnits.doo"] = UnitsDoc.Write();
        if (Pathing != null)
            files["war3map.wpm"] = Pathing.SaveBytes();
        if (Regions != null)
            files["war3map.w3r"] = Regions.SaveBytes();
        if (MapIdentity != null)
            files["war3map.w3i"] = MapIdentity.Write();
        return files;
    }

    /// <summary>
    /// Writes a d1map snapshot without changing the current session format or
    /// source path. Used by conversion and engine preview boundaries.
    /// </summary>
    public string SaveNativeCopy(string folder, string? name = null)
    {
        if (Map == null)
            throw new InvalidOperationException("未打开地图");
        folder = Path.GetFullPath(folder);
        NativeMapFormat.Save(
            CaptureNative(name ?? Path.GetFileName(folder)),
            folder);
        return folder;
    }

    /// <summary>Switches the current session to a newly written d1map folder.</summary>
    public string SaveAsNative(string folder, string? name = null)
    {
        folder = Path.GetFullPath(folder);
        NativeMap native = CaptureNative(name ?? Path.GetFileName(folder));
        NativeMapFormat.Save(native, folder);
        SourcePath = folder;
        W3ePath = "";
        FormatKind = MapFormatKind.D1MapFolder;
        _nativeTemplate = native;
        Pathing = null;
        Regions = null;
        Info = null;
        MapIdentity = null;
        IsDirty = false;
        return folder;
    }

    private NativeMap CaptureNative(string name) =>
        NativeMapBridge.FromEditorModel(
            name,
            Map ?? throw new InvalidOperationException("未打开地图"),
            Doodads.Concat(DoodadsDoc.ReadSpecialPlacements(
                Map!.Width,
                Map.Height,
                Map.CenterOffsetX,
                Map.CenterOffsetY,
                (x, y) => Map.Corners[y * Map.Width + x].WorldY(),
                W3EMap.TileSize)),
            Units,
            _nativeTemplate);

    private static void WriteWithBackup(string path, byte[] bytes)
    {
        var bak = path + ".bak";
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            // Flush the payload to disk before it replaces the target: a rename
            // alone can be journaled ahead of unflushed data blocks on power loss.
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Copy(path, bak, overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    /// <summary>Next unique creation number across doodads and units.</summary>
    public int NextCreationNumber() =>
        Math.Max(DoodadsDoc.NextCreationNumber(), UnitsDoc.NextCreationNumber());

    public void MarkDirty() => IsDirty = true;

    public void MarkPathingDirty()
    {
        _pathingDirty = true;
        IsDirty = true;
    }

    public void UpdateMapIdentity(W3MapInfoIdentity identity)
    {
        MapIdentity = identity;
        _mapIdentityDirty = true;
        IsDirty = true;
    }

    public void MarkRegionsDirty()
    {
        _regionsDirty = true;
        IsDirty = true;
    }

    /// <summary>Creates an empty 32-world-unit WPM aligned to the terrain.</summary>
    public W3PathingMap EnsurePathing()
    {
        if (Map == null)
            throw new InvalidOperationException("未打开地图");
        if (IsNativeFormat)
            throw new InvalidOperationException("d1map 当前不承载 War3 WPM 数据");
        Pathing ??= new W3PathingMap
        {
            Width = checked(Map.TileCountX * 4),
            Height = checked(Map.TileCountY * 4),
            Cells = new byte[checked(Map.TileCountX * Map.TileCountY * 16)],
        };
        return Pathing;
    }

    /// <summary>Creates an empty war3map.w3r model for folder maps.</summary>
    public W3RegionsFile EnsureRegions()
    {
        if (Map == null)
            throw new InvalidOperationException("未打开地图");
        if (FormatKind != MapFormatKind.War3Folder)
            throw new InvalidOperationException("区域编辑仅适用于已解压的 War3 地图目录");
        return Regions ??= new W3RegionsFile();
    }

    /// <summary>
    /// Resizes equally from opposite sides, following HiveWE Map::resize and
    /// Terrain::resize_corner_data. Existing edge values are clamped into newly
    /// added space; WPM cells use the same four-cells-per-tile deltas.
    /// </summary>
    public void Resize(int widthTiles, int heightTiles)
    {
        if (Map == null)
            throw new InvalidOperationException("未打开地图");
        if (!W3EMap.IsValidNewSize(widthTiles) || !W3EMap.IsValidNewSize(heightTiles))
            throw new ArgumentOutOfRangeException(nameof(widthTiles),
                $"地图尺寸必须在 {W3EMap.MinTiles}–{W3EMap.MaxTiles} 之间且为 {W3EMap.TileStep} 的倍数。");

        int oldTilesX = Map.TileCountX, oldTilesY = Map.TileCountY;
        if (widthTiles == oldTilesX && heightTiles == oldTilesY)
            return;

        int deltaX = widthTiles - oldTilesX, deltaY = heightTiles - oldTilesY;
        int deltaLeft = (int)Math.Floor(deltaX / 2.0);
        int deltaBottom = (int)Math.Floor(deltaY / 2.0);
        int newWidth = widthTiles + 1, newHeight = heightTiles + 1;
        W3Corner[] oldCorners = Map.Corners;
        int oldWidth = Map.Width, oldHeight = Map.Height;
        var resized = new W3Corner[checked(newWidth * newHeight)];
        var rng = new Random();

        for (int y = 0; y < newHeight; y++)
        for (int x = 0; x < newWidth; x++)
        {
            int sourceX = x - deltaLeft, sourceY = y - deltaBottom;
            bool fromOld = sourceX >= 0 && sourceY >= 0 && sourceX < oldWidth && sourceY < oldHeight;
            sourceX = Math.Clamp(sourceX, 0, oldWidth - 1);
            sourceY = Math.Clamp(sourceY, 0, oldHeight - 1);
            W3Corner source = oldCorners[sourceY * oldWidth + sourceX];
            resized[y * newWidth + x] = CloneCorner(source,
                mapEdge: x == 0 || y == 0 || x == newWidth - 1 || y == newHeight - 1,
                groundVariation: fromOld ? source.GroundVariation : W3EMap.RandomGroundVariation(rng));
        }

        Map.Width = newWidth;
        Map.Height = newHeight;
        Map.CenterOffsetX -= deltaLeft * W3EMap.TileSize;
        Map.CenterOffsetY -= deltaBottom * W3EMap.TileSize;
        Map.Corners = resized;

        if (Pathing != null)
        {
            int leftCells = deltaLeft * 4, bottomCells = deltaBottom * 4;
            int oldPathWidth = Pathing.Width, oldPathHeight = Pathing.Height;
            byte[] oldCells = Pathing.Cells;
            int newPathWidth = widthTiles * 4, newPathHeight = heightTiles * 4;
            var cells = new byte[checked(newPathWidth * newPathHeight)];
            for (int y = 0; y < newPathHeight; y++)
            for (int x = 0; x < newPathWidth; x++)
            {
                int sourceX = Math.Clamp(x - leftCells, 0, oldPathWidth - 1);
                int sourceY = Math.Clamp(y - bottomCells, 0, oldPathHeight - 1);
                cells[y * newPathWidth + x] = oldCells[sourceY * oldPathWidth + sourceX];
            }
            Pathing.Width = newPathWidth;
            Pathing.Height = newPathHeight;
            Pathing.Cells = cells;
            _pathingDirty = true;
        }
        IsDirty = true;
    }

    private static W3Corner CloneCorner(W3Corner source, bool mapEdge, int groundVariation) => new()
    {
        GroundHeight = source.GroundHeight,
        WaterHeight = source.WaterHeight,
        MapEdge = mapEdge,
        Ramp = source.Ramp,
        Blight = source.Blight,
        Water = source.Water,
        Boundary = source.Boundary,
        GroundTexture = source.GroundTexture,
        CliffVariation = source.CliffVariation,
        GroundVariation = groundVariation,
        CliffTexture = source.CliffTexture,
        LayerHeight = source.LayerHeight,
    };

    // ---- TileSetter operations (HiveWE TileSetter semantics) ----

    /// <summary>
    /// Ground tileset limit: classic w3e (v11) stores 4-bit texture indices (16),
    /// Reforged v12+ stores 6-bit indices (64, HiveWE terrain.ixx).
    /// </summary>
    public int MaxGroundTextures =>
        IsNativeFormat || Map is { Version: >= 12 } ? 64 : 16;

    public int TextureUsageCount(int index)
    {
        if (Map == null) return 0;
        int n = 0;
        foreach (var c in Map.Corners)
            if (c.GroundTexture == index)
                n++;
        return n;
    }

    /// <summary>Swaps palette entries i and j and remaps every corner accordingly.</summary>
    public void SwapGroundTextures(int i, int j)
    {
        if (Map == null || i == j) return;
        if (i < 0 || j < 0 || i >= Map.GroundTilesets.Count || j >= Map.GroundTilesets.Count) return;
        (Map.GroundTilesets[i], Map.GroundTilesets[j]) = (Map.GroundTilesets[j], Map.GroundTilesets[i]);
        foreach (var c in Map.Corners)
        {
            if (c.GroundTexture == i) c.GroundTexture = j;
            else if (c.GroundTexture == j) c.GroundTexture = i;
        }
        MarkDirty();
    }

    /// <summary>Removes an unused palette entry; throws when corners still reference it.</summary>
    public void RemoveGroundTexture(int index)
    {
        if (Map == null) return;
        if (index < 0 || index >= Map.GroundTilesets.Count) return;
        int used = TextureUsageCount(index);
        if (used > 0)
            throw new InvalidOperationException($"图块 {Map.GroundTilesets[index]} 仍被 {used} 个角点使用，无法移除。");
        Map.GroundTilesets.RemoveAt(index);
        foreach (var c in Map.Corners)
            if (c.GroundTexture > index)
                c.GroundTexture--;
        MarkDirty();
    }

    public void AddGroundTexture(string fourcc)
    {
        if (Map == null) return;
        if (fourcc.Length != 4)
            throw new InvalidOperationException($"图块 ID 必须是 4 字符：{fourcc}");
        if (Map.GroundTilesets.Contains(fourcc))
            throw new InvalidOperationException($"图块 {fourcc} 已在使用中。");
        if (Map.GroundTilesets.Count >= MaxGroundTextures)
            throw new InvalidOperationException($"图块集已满（上限 {MaxGroundTextures} 个）。");
        Map.GroundTilesets.Add(fourcc);
        MarkDirty();
    }
}
