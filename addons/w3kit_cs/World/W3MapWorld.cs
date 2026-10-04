using System;
using Godot;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Archives;
using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Formats;
using Diablo1Like.W3.Mdx;
using Diablo1Like.W3.Pathing;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.Terrain;

namespace Diablo1Like.W3.World;

/// <summary>
/// 加载已导入的 .res 关卡、解包的地图目录或原始 .w3e；HiveWE 风格：GPU 实例化地形/水面、
/// 批量 MDX、视锥剔除、地形优先再流式加载道具。迁移自 addons/w3kit/w3_map_world.gd，
/// 不再区分 GDS/C# 地形双轨——统一使用 Warsmash* C# 渲染器。
/// R6：不再 GD.Load(.gd) 门面，直接构造/调用对应 C# 桥（W3EMapBridge/W3HeightSamplerBridge/
/// W3TextureProvider/W3GridPathfinderBridge/W3PathingGridBridge/W3MapArchiveBridge/
/// W3DooParserBridge/W3ObjectCatalog/W3MapInfoVersionBridge/NativeMapLoader）。
/// 大量成员保持小写下划线命名（与 GDS 属性同名），因为仍是 GDS 的 w3_sandbox.gd /
/// w3_game.gd / 多个 tools/*.gd 通过 "." 动态属性访问（Object.Get/Set 鸭子类型，
/// 不需要 [Export] 也能工作，但保留同名是为了零改动兼容旧调用点）。
/// </summary>
[GlobalClass]
public partial class W3MapWorld : Node3D
{
    [Signal]
    public delegate void LoadFinishedEventHandler(bool ok, string message);

    [Export] public bool terrain_only;
    /// <summary>预览：跳过方向光阴影（HiveWE 编辑器不承担游戏内阴影开销）。</summary>
    [Export] public bool preview_no_shadows;
    /// <summary>仅供预览使用的退路；测试与正式产品应保持关闭。</summary>
    [Export] public bool allow_procedural_fallback;
    /// <summary>为 true 时，地面移动单位交给模拟层（unit_placements），不再烘焙进批次+寻路覆盖层。</summary>
    [Export] public bool simulate_units;

    // 仍是 GDScript RefCounted/Resource 的动态数据对象，只能用 GodotObject 承载。
    public GodotObject? archive;
    public GodotObject? map;
    public GodotObject? sampler;
    public GodotObject? pathfinder;
    /// <summary>war3map.wpm 的 Warsmash 风格静态寻路网格（不可用时为 null）。</summary>
    public GodotObject? pathing;
    public GodotObject? textures;
    public GodotObject? catalog;

    public WarsmashGroundRenderer? Terrain;
    public WarsmashCliffRenderer? Cliffs;
    public W3WaterRenderer? Water;
    public W3MdxBatcher? Batcher;

    // 小写别名：供 GDScript "." 动态访问（w3_sandbox.gd 等）与既有习惯保持一致。
    public WarsmashGroundRenderer? terrain => Terrain;
    public WarsmashCliffRenderer? cliffs => Cliffs;
    public W3WaterRenderer? water => Water;
    public W3MdxBatcher? batcher => Batcher;

    /// <summary>供游戏层的 W3DooParser.Placement 列表（simulate_units 时填充）。</summary>
    public Godot.Collections.Array unit_placements = new();

    private string _status = "";
    private string _loadPathLabel = "";
    private int _gameVersionMajor;
    private int _gameVersionMinor;

    public override void _Ready()
    {
        var textureProvider = new W3TextureProvider();
        textureProvider.LoadTerrainSlk();
        textures = textureProvider;
        catalog = new W3ObjectCatalog();
        Terrain = new WarsmashGroundRenderer { Name = "Terrain" };
        AddChild(Terrain);
        Cliffs = new WarsmashCliffRenderer { Name = "Cliffs" };
        AddChild(Cliffs);
        Water = new W3WaterRenderer { Name = "Water" };
        AddChild(Water);
        Batcher = new W3MdxBatcher { Name = "MdxBatches" };
        AddChild(Batcher);
    }

    public bool load_path(string path)
    {
        _clear_visuals();
        _reset_loaded_state();
        new W3PathResolverBridge().ClearResolveCache();
        _loadPathLabel = path;
        string ext = path.GetExtension().ToLowerInvariant();
        if (ext is "res" or "tres")
        {
            string resourcePath = path;
            if (!resourcePath.StartsWith("res://") && !resourcePath.StartsWith("user://"))
                resourcePath = ProjectSettings.LocalizePath(resourcePath);
            Resource? level = ResourceLoader.Load(resourcePath);
            return load_level_data(level);
        }
        string nativeDir = path;
        if (path.GetFile().ToLowerInvariant() == "map.json")
            nativeDir = path.GetBaseDir();
        if (DirAccess.DirExistsAbsolute(nativeDir) && FileAccess.FileExists(nativeDir.PathJoin("map.json")))
            return LoadNative(nativeDir);

        var archiveBridge = new W3MapArchiveBridge();
        archive = archiveBridge;
        if (string.IsNullOrEmpty(path))
            return LoadFailure("empty map path");
        if (!archiveBridge.Open(path))
        {
            _status = $"W3X/map open failed: {path} ({archiveBridge.ExtractError})";
            return FallbackOrFail(_status);
        }
        Godot.Collections.Dictionary mapInfo = W3MapInfoVersionBridge.ParseStatic(archiveBridge.ReadFile("war3map.w3i"));
        _gameVersionMajor = DictGet(mapInfo, "game_version_major", 0).AsInt32();
        _gameVersionMinor = DictGet(mapInfo, "game_version_minor", 0).AsInt32();
        string mapDir = archiveBridge.MapDir;
        var textureProvider = (W3TextureProvider)textures!;
        textureProvider.SetMapDir(mapDir);
        textureProvider.LoadTerrainSlk();
        ((W3ObjectCatalog)catalog!).LoadFromRoots(archive);

        byte[] w3eBytes = archiveBridge.ReadFile("war3map.w3e");
        W3EMapBridge? mapBridge = w3eBytes.Length == 0
            ? W3EMapBridge.LoadFileStatic(mapDir.PathJoin("war3map.w3e"))
            : W3EMapBridge.LoadBytesStatic(w3eBytes);
        map = mapBridge;
        if (map == null)
            return FallbackOrFail($"failed to parse war3map.w3e: {W3EMapBridge.LastError}");
        var samplerBridge = new W3HeightSamplerBridge();
        samplerBridge.BindMap(map);
        sampler = samplerBridge;
        pathfinder = new W3GridPathfinderBridge(sampler);
        LoadPathingGrid(archiveBridge.ReadFile("war3map.wpm"), mapDir);

        // 1-2：地形+水面实例化(快路径)；悬崖仍走共享 MDX 实例。
        byte[] shdBytes = archiveBridge.ReadFile("war3map.shd");
        if (shdBytes.Length == 0)
        {
            string shdPath = mapDir.PathJoin("war3map.shd");
            if (FileAccess.FileExists(shdPath))
            {
                using FileAccess? f = FileAccess.Open(shdPath, FileAccess.ModeFlags.Read);
                if (f != null)
                    shdBytes = f.GetBuffer((long)f.GetLength());
            }
        }
        BuildTerrainVisuals(map, textures, archive, shdBytes);

        if (preview_no_shadows)
            DisableSceneShadows();

        // 3-4：把道具加入 MultiMesh 批处理队列(流式+视锥剔除)。
        if (!terrain_only)
        {
            Batcher!.setup(textures, catalog, archive, sampler);
            QueueDoodads();
            Batcher.begin_streaming();
        }
        else
        {
            GD.Print("W3MapWorld: terrain_only — skipped doodads");
        }

        RefreshStatus();
        GD.Print(_status);
        EmitSignal(SignalName.LoadFinished, true, _status);
        return true;
    }

    private bool LoadNative(string folder)
    {
        Godot.Collections.Dictionary loaded = new NativeMapLoader().LoadFolder(folder);
        if (loaded.Count == 0 || !loaded.ContainsKey("map") || loaded["map"].AsGodotObject() == null)
            return LoadFailure($"failed to load d1map: {folder} ({DictGet(loaded, "error", "missing map data").AsString()})");

        // 复用现有的呈现/资产管线，但这条路径永不解析 war3map.w3e/doo。
        var archiveBridge = new W3MapArchiveBridge { MapDir = folder.TrimEnd('/'), SourcePath = folder };
        archive = archiveBridge;
        map = loaded["map"].AsGodotObject();
        _gameVersionMajor = 0;
        _gameVersionMinor = 0;
        string mapDir = archiveBridge.MapDir;
        var textureProvider = (W3TextureProvider)textures!;
        textureProvider.SetMapDir(mapDir);
        textureProvider.LoadTerrainSlk();
        ((W3ObjectCatalog)catalog!).LoadFromRoots(archive);
        var samplerBridge = new W3HeightSamplerBridge();
        samplerBridge.BindMap(map);
        sampler = samplerBridge;
        pathfinder = new W3GridPathfinderBridge(sampler);
        pathing = null;

        BuildTerrainVisuals(map, textures, archive, Array.Empty<byte>());
        if (preview_no_shadows)
            DisableSceneShadows();

        Godot.Collections.Array placements = DictGet(loaded, "placements", new Godot.Collections.Array()).AsGodotArray();
        if (!terrain_only)
        {
            Batcher!.setup(textures, catalog, archive, sampler);
            QueueNativePlacements(placements);
            Batcher.begin_streaming();
        }
        RefreshStatus();
        _status += $" | d1map placements={placements.Count}";
        int width = map.Get("width").AsInt32();
        int height = map.Get("height").AsInt32();
        GD.Print($"[D1Map] loaded name={DictGet(loaded, "name", "").AsString()} corners={width * height} placements={placements.Count}");
        GD.Print(_status);
        EmitSignal(SignalName.LoadFinished, true, _status);
        return true;
    }

    /// <summary>W3LevelImporter 产出数据的运行时路径。这里不解析 W3E。</summary>
    public bool load_level_data(Resource? level)
    {
        _clear_visuals();
        _reset_loaded_state();
        new W3PathResolverBridge().ClearResolveCache();
        if (level == null || !level.HasMethod("is_valid") || !level.Call("is_valid").AsBool())
            return LoadFailure("invalid imported W3LevelData");
        _loadPathLabel = level.Get("source_path").AsString();
        archive = level;
        map = level;
        _gameVersionMajor = level.Get("game_version_major").AsInt32();
        _gameVersionMinor = level.Get("game_version_minor").AsInt32();
        var textureProvider = (W3TextureProvider)textures!;
        textureProvider.SetMapDir("");
        textureProvider.LoadTerrainSlk();
        ((W3ObjectCatalog)catalog!).LoadFromRoots(level);
        var samplerBridge = new W3HeightSamplerBridge();
        samplerBridge.BindMap(map);
        sampler = samplerBridge;
        pathfinder = new W3GridPathfinderBridge(sampler);
        LoadPathingGrid(level.Call("read_file", "war3map.wpm").AsByteArray(), "");
        byte[] shadowBytes = level.Get("shadow_bytes").AsByteArray();
        BuildTerrainVisuals(map, textures, level, shadowBytes);
        if (preview_no_shadows)
            DisableSceneShadows();
        if (!terrain_only)
        {
            Batcher!.setup(textures, catalog, level, sampler);
            QueueDoodads();
            Batcher.begin_streaming();
        }
        RefreshStatus();
        GD.Print(_status);
        EmitSignal(SignalName.LoadFinished, true, _status);
        return true;
    }

    private bool FallbackOrFail(string message)
    {
        if (allow_procedural_fallback)
        {
            GD.PushWarning(message);
            return LoadProcedural(message);
        }
        return LoadFailure(message);
    }

    private bool LoadFailure(string message)
    {
        _status = message;
        GD.PushError($"W3MapWorld: {message}");
        EmitSignal(SignalName.LoadFinished, false, _status);
        return false;
    }

    private void LoadPathingGrid(byte[] wpmBytes, string mapDir)
    {
        pathing = null;
        if (wpmBytes.Length == 0 && !string.IsNullOrEmpty(mapDir))
        {
            string wpmPath = mapDir.PathJoin("war3map.wpm");
            if (FileAccess.FileExists(wpmPath))
            {
                using FileAccess? f = FileAccess.Open(wpmPath, FileAccess.ModeFlags.Read);
                if (f != null)
                    wpmBytes = f.GetBuffer((long)f.GetLength());
            }
        }
        if (wpmBytes.Length == 0)
        {
            GD.Print("W3MapWorld: no war3map.wpm — pathing grid unavailable");
            return;
        }
        Vector2 centerOffset = map!.Get("center_offset").AsVector2();
        pathing = W3PathingGridBridge.LoadBytes(wpmBytes, centerOffset);
        if (pathing == null)
            GD.PushWarning($"W3MapWorld: wpm parse failed: {W3PathingGridBridge.LastError}");
        else
            GD.Print($"W3MapWorld: pathing grid {pathing.Get("width").AsInt32()}x{pathing.Get("height").AsInt32()}");
    }

    private void BuildTerrainVisuals(GodotObject mapResource, GodotObject textureProvider,
        GodotObject? resolver, byte[] shadows)
    {
        Cliffs!.SetPathResolver(resolver);
        Cliffs.Build(mapResource, textureProvider, shadows);
        Terrain!.BuildFrom(Cliffs.BuildResult!, textureProvider, shadows);
        Water!.Build(mapResource, textureProvider);
    }

    private void _reset_loaded_state()
    {
        archive = null;
        map = null;
        sampler = null;
        pathfinder = null;
        pathing = null;
        unit_placements = new Godot.Collections.Array();
        _status = "";
        _gameVersionMajor = 0;
        _gameVersionMinor = 0;
    }

    public bool load_procedural(int tilesX = 24, int tilesY = 24) => LoadProcedural("procedural test map", tilesX, tilesY);

    private bool LoadProcedural(string msg, int tilesX = 24, int tilesY = 24)
    {
        _clear_visuals();
        _reset_loaded_state();
        map = W3EMapBridge.MakeTestMap(tilesX, tilesY);
        var textureProvider = new W3TextureProvider();
        textures = textureProvider;
        var catalogBridge = new W3ObjectCatalog();
        catalog = catalogBridge;
        catalogBridge.LoadFromRoots(null);
        var samplerBridge = new W3HeightSamplerBridge();
        samplerBridge.BindMap(map);
        sampler = samplerBridge;
        pathfinder = new W3GridPathfinderBridge(sampler);
        BuildTerrainVisuals(map, textures, null, Array.Empty<byte>());
        _status = msg;
        GD.Print("W3MapWorld: ", _status);
        EmitSignal(SignalName.LoadFinished, true, _status);
        return true;
    }

    public string get_status() => _status;

    // --- 地形边界 API（游戏逻辑不应直接触碰内部结构） -----------------------

    public bool is_loaded() => map != null && sampler != null;

    /// <summary>W3 地面平面上的 FOOT 可行走性（静态网格 + 烘焙覆盖层）；没有寻路网格时视为全部可走。</summary>
    public bool is_walkable(float w3X, float w3Y, float collisionSize = 0.0f)
    {
        if (pathing == null)
            return true;
        return pathing.Call("is_pathable_foot", w3X, w3Y, collisionSize).AsBool();
    }

    /// <summary>W3 地面平面某点的 Godot-Y 地形高度（未加载时为 0）。</summary>
    public float ground_height_at(float w3X, float w3Y)
    {
        if (sampler == null)
            return 0.0f;
        return sampler.Call("sample_y", w3X, w3Y).AsSingle();
    }

    /// <summary>把相机射线打到采样地形上；返回 Godot 空间的落点，射线出图时返回 null。</summary>
    public Variant pick_ground(Camera3D camera, Vector2 screenPos)
    {
        if (sampler == null || camera == null)
            return new Variant();
        Vector3 from = camera.ProjectRayOrigin(screenPos);
        Vector3 dir = camera.ProjectRayNormal(screenPos);
        float prevT = 0.0f;
        for (int i = 0; i < 400; i++)
        {
            float t = prevT + 25.0f;
            Vector3 p = from + dir * t;
            float dy = p.Y - sampler.Call("sample_y", p.X, p.Z).AsSingle();
            if (dy <= 0.0f)
            {
                float lo = prevT;
                float hi = t;
                for (int j = 0; j < 12; j++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 mp = from + dir * mid;
                    if (mp.Y - sampler.Call("sample_y", mp.X, mp.Z).AsSingle() <= 0.0f)
                        hi = mid;
                    else
                        lo = mid;
                }
                Vector3 hp = from + dir * hi;
                return new Vector3(hp.X, sampler.Call("sample_y", hp.X, hp.Z).AsSingle(), hp.Z);
            }
            prevT = t;
        }
        return new Variant();
    }

    private void DisableSceneShadows()
    {
        Node? root = GetTree()?.CurrentScene;
        root ??= GetParent();
        if (root == null)
            return;
        foreach (Node n in root.FindChildren("*", "DirectionalLight3D", true, false))
            if (n is DirectionalLight3D light)
                light.ShadowEnabled = false;
    }

    private void RefreshStatus()
    {
        Godot.Collections.Dictionary cliffStats = Cliffs != null ? Cliffs.GetCounts() : new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary bcounts = Batcher != null ? Batcher.get_counts() : new Godot.Collections.Dictionary();
        int tilesX = map != null ? map.Call("tile_count_x").AsInt32() : 0;
        int tilesY = map != null ? map.Call("tile_count_y").AsInt32() : 0;
        int waterTiles = Water?.GetWaterTileCount() ?? 0;
        _status = $"GPU map {tilesX}x{tilesY} | props ok={DictGet(bcounts, "ok", 0)} miss={DictGet(bcounts, "miss", 0)} batches={DictGet(bcounts, "batches", 0)} | cliffs ok={DictGet(cliffStats, "ok", 0)} batches={DictGet(cliffStats, "batches", 0)} | water={waterTiles}{(terrain_only ? " [terrain preview]" : "")}";
    }

    private void _clear_visuals()
    {
        Batcher?.clear();
        if (Terrain != null)
            foreach (Node c in Terrain.GetChildren())
                c.QueueFree();
        if (Cliffs != null)
            foreach (Node c in Cliffs.GetChildren())
                c.QueueFree();
        if (Water != null)
            foreach (Node c in Water.GetChildren())
                c.QueueFree();
    }

    private void QueueDoodads()
    {
        if (archive == null)
            return;
        byte[] data = archive.Call("read_file", "war3map.doo").AsByteArray();
        Godot.Collections.Array doodads = data.Length > 0
            ? W3DooParserBridge.ParseDoodads(data, _gameVersionMajor, _gameVersionMinor)
            : new Godot.Collections.Array();
        byte[] unitData = archive.Call("read_file", "war3mapUnits.doo").AsByteArray();
        Godot.Collections.Array units = unitData.Length > 0
            ? W3DooParserBridge.ParseUnits(unitData, _gameVersionMajor, _gameVersionMinor)
            : new Godot.Collections.Array();
        GD.Print($"W3MapWorld: doodads={doodads.Count} units={units.Count}");
        foreach (Variant pv in doodads)
            Batcher!.enqueue_placement(pv.AsGodotObject());
        int blocked = 0;
        foreach (Variant pv in units)
        {
            GodotObject p = pv.AsGodotObject();
            if (simulate_units && IsSimulatedUnit(p))
            {
                unit_placements.Add(p);
                continue;
            }
            Batcher!.enqueue_placement(p);
            if (BlitUnitPathing(p))
                blocked++;
        }
        if (blocked > 0)
            GD.Print($"W3MapWorld: pathing overlay from {blocked} ground units");
        if (unit_placements.Count > 0)
            GD.Print($"W3MapWorld: {unit_placements.Count} units handed to simulation layer");
    }

    private void QueueNativePlacements(Godot.Collections.Array placements)
    {
        int doodads = 0;
        int units = 0;
        int blocked = 0;
        foreach (Variant pv in placements)
        {
            GodotObject p = pv.AsGodotObject();
            if (p.Get("is_unit").AsBool())
            {
                units++;
                if (simulate_units && IsSimulatedUnit(p))
                {
                    unit_placements.Add(p);
                    continue;
                }
                Batcher!.enqueue_placement(p);
                if (BlitUnitPathing(p))
                    blocked++;
            }
            else
            {
                doodads++;
                Batcher!.enqueue_placement(p);
            }
        }
        GD.Print($"W3MapWorld(d1map): doodads={doodads} units={units}");
        if (blocked > 0)
            GD.Print($"W3MapWorld: pathing overlay from {blocked} ground units");
        if (unit_placements.Count > 0)
            GD.Print($"W3MapWorld: {unit_placements.Count} units handed to simulation layer");
    }

    /// <summary>地面移动单位变成实时 W3SimUnits（动态碰撞），而不是烘焙进寻路覆盖层的静态批处理雕像。</summary>
    private bool IsSimulatedUnit(GodotObject? p)
    {
        if (p == null || !p.Get("is_unit").AsBool() || catalog == null)
            return false;
        string id = p.Get("id").AsString();
        if (id is "sloc" or "ncam" or "Start")
            return false;
        if (catalog.Call("unit_move_height", id).AsSingle() > 0.0f)
            return false;
        string moveType = catalog.Call("unit_move_type", id).AsString();
        return moveType is "foot" or "horse" or "amph" or "hover";
    }

    /// <summary>把已放置单位的 CWorldCollision 方块烘焙进寻路覆盖层，让模拟英雄与地图单位碰撞
    /// （Warsmash 用四叉树保存这些数据；静态预览单位等价于烘焙格子）。</summary>
    private bool BlitUnitPathing(GodotObject? p)
    {
        if (pathing == null || catalog == null || p == null)
            return false;
        string id = p.Get("id").AsString();
        if (id is "sloc" or "ncam" or "Start")
            return false;
        string moveType = catalog.Call("unit_move_type", id).AsString();
        if (moveType == "fly" || catalog.Call("unit_move_height", id).AsSingle() > 0.0f)
            return false;
        float collision = catalog.Call("unit_collision", id).AsSingle();
        if (collision <= 0.0f)
            return false;
        Vector3 pos = p.Get("position").AsVector3();
        pathing.Call("blit_unit_collision", pos.X, pos.Z, collision);
        return true;
    }

    /// <summary>Godot.Collections.Dictionary 没有 GetValueOrDefault（那是 .NET 泛型 Dictionary 的扩展方法）。</summary>
    private static Variant DictGet(Godot.Collections.Dictionary dict, string key, Variant fallback) =>
        dict.TryGetValue(key, out Variant value) ? value : fallback;
}
