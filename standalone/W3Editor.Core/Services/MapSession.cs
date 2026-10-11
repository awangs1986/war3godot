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
    public W3MapInfoVersion? Info { get; private set; }
    /// <summary>Unsaved edits (w3e or placements).</summary>
    public bool IsDirty { get; private set; }
    public MapFormatKind FormatKind { get; private set; }
    public bool IsNativeFormat => FormatKind == MapFormatKind.D1MapFolder;
    private NativeMap? _nativeTemplate;

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
            Info = null;
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
        Info = null;

        if (folder == null)
            return;

        // Sidecar files are best-effort: a damaged doo must not block the map.
        Info = TryRead(folder, "war3map.w3i", W3MapInfoVersion.Parse);
        bool hasSkins = Info?.HasReforgedSkinIds ?? false;
        DoodadsDoc = TryRead(folder, "war3map.doo", b => W3DooFile.ParseDoodadsDocument(b, hasSkins))
            ?? new W3DooDocument { IsUnits = false, HasSkinIds = hasSkins };
        UnitsDoc = TryRead(folder, "war3mapUnits.doo", b => W3DooFile.ParseUnitsDocument(b, hasSkins))
            ?? new W3DooDocument { IsUnits = true, HasSkinIds = hasSkins };
        Pathing = TryRead(folder, "war3map.wpm", W3PathingMap.LoadBytes);
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
    /// back to disk. Each file keeps a one-shot .bak of its original.
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
        }
        IsDirty = false;
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
        Info = null;
        IsDirty = false;
        return folder;
    }

    private NativeMap CaptureNative(string name) =>
        NativeMapBridge.FromEditorModel(
            name,
            Map ?? throw new InvalidOperationException("未打开地图"),
            Doodads.Concat(DoodadsDoc.ReadSpecialPlacements(Map)),
            Units,
            _nativeTemplate);

    private static void WriteWithBackup(string path, byte[] bytes)
    {
        var bak = path + ".bak";
        if (File.Exists(path) && !File.Exists(bak))
            File.Copy(path, bak);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Next unique creation number across doodads and units.</summary>
    public int NextCreationNumber() =>
        Math.Max(DoodadsDoc.NextCreationNumber(), UnitsDoc.NextCreationNumber());

    public void MarkDirty() => IsDirty = true;

    // ---- TileSetter operations (HiveWE TileSetter semantics) ----

    /// <summary>
    /// Ground tileset limit: classic w3e (v11) stores 4-bit texture indices (16),
    /// Reforged v12+ stores 6-bit indices (64, HiveWE terrain.ixx).
    /// </summary>
    public int MaxGroundTextures => Map is { Version: >= 12 } ? 64 : 16;

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
