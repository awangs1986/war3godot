using System;
using System.Collections.Generic;
using Godot;
using Diablo1Like.W3.Paths;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// HiveWE 风格的 MDX 批量渲染：静态道具按 (model,geoset) 合成 MultiMesh，
/// 动态流式加载模型、视锥剔除；对应旧版 addons/w3kit/w3_mdx_batcher.gd。
/// model 现为 C# W3MdxModel（RefCounted），仍通过 Get()/Call() 鸭子类型访问
/// 以复用既有字段名；catalog/archive/sampler 也是通过 Get()/Call() 动态访问的
/// C# 桥对象。方法名保持 snake_case 以兼容既有调用点（GDScript 探针工具直接
/// preload 本文件 .new() 使用）。
/// </summary>
[GlobalClass]
public partial class W3MdxBatcher : Node3D
{
    private const int MaxStatic = 20000;
    private const int BatchLoadPerFrame = 10;
    private const float PropDrawDist = 5200.0f;
    private const float FrustumCheckSec = 0.08f;
    private const float FrustumMargin = 192.0f;
    private const int AnimSpawnPerFrame = 4;
    private const float AnimNearDist = 800.0f;
    private const float AnimFarDist = 950.0f;
    private const float AnimDrawDist = 4200.0f;
    private const int MaxNearAnims = 48;
    private const float ProximityCheckSec = 0.1f;

    public GodotObject textures;
    public GodotObject catalog;
    public GodotObject archive;
    public GodotObject sampler;

    private readonly GArray _queue = new();
    private readonly List<string> _model_jobs = new();
    public GDictionary _model_cache = new();
    private readonly GDictionary _model_radius_cache = new();
    private readonly GDictionary _mesh_cache = new();
    public GDictionary _batches = new();
    private int _ok;
    private int _miss;
    private int _fail;
    private bool _flushed;
    private float _frustum_timer;
    private int _visible_static;
    private readonly GArray _anim_pending = new();
    public GArray _anim_instances = new();
    private Node3D _anim_root;
    private float _proximity_timer;
    private int _anim_active_n;
    private int _lod_level;
    private int _load_total_models;
    private int _load_total_placements;
    private int _load_total_animations;
    private Node3D _selectable_units_root;

    private static readonly W3PathResolverBridge Paths = new();

    private static GArray GA(GodotObject o, string p) => o.Get(p).AsGodotArray();
    private static string GS(GodotObject o, string p) => o.Get(p).AsString();
    private static int GI(GodotObject o, string p) => o.Get(p).AsInt32();
    private static float GF(GodotObject o, string p) => o.Get(p).AsSingle();
    private static Vector3 GV3(GodotObject o, string p) => o.Get(p).AsVector3();
    private static Vector3[] GV3A(GodotObject o, string p) => o.Get(p).AsVector3Array();
    private static Vector2[] GV2A(GodotObject o, string p) => o.Get(p).AsVector2Array();
    private static int[] GI32A(GodotObject o, string p) => o.Get(p).AsInt32Array();
    private static float[] GF32A(GodotObject o, string p) => o.Get(p).AsFloat32Array();

    public void setup(GodotObject pTextures, GodotObject pCatalog, GodotObject pArchive, GodotObject pSampler)
    {
        textures = pTextures;
        catalog = pCatalog;
        archive = pArchive;
        sampler = pSampler;
    }

    public void clear()
    {
        _queue.Clear();
        _model_jobs.Clear();
        _batches.Clear();
        _mesh_cache.Clear();
        _model_radius_cache.Clear();
        _ok = 0;
        _miss = 0;
        _fail = 0;
        _flushed = false;
        _frustum_timer = 0f;
        _visible_static = 0;
        _anim_pending.Clear();
        _anim_instances.Clear();
        _anim_root = null;
        _proximity_timer = 0f;
        _anim_active_n = 0;
        _lod_level = 0;
        _load_total_models = 0;
        _load_total_placements = 0;
        _load_total_animations = 0;
        _selectable_units_root = null;
        foreach (Node c in GetChildren())
            c.QueueFree();
        SetProcess(false);
    }

    public GDictionary get_counts() => new()
    {
        ["ok"] = _ok,
        ["miss"] = _miss,
        ["fail"] = _fail,
        ["queued"] = _queue.Count,
        ["jobs"] = _model_jobs.Count,
        ["batches"] = _batches.Count,
        ["visible_static"] = _visible_static,
        ["animated"] = _anim_instances.Count,
        ["anim_active"] = _anim_active_n,
        ["anim_pending"] = _anim_pending.Count,
        ["lod"] = _lod_level,
    };

    public bool is_streaming_complete() => _flushed && _model_jobs.Count == 0 && _queue.Count == 0 && _anim_pending.Count == 0;

    public GDictionary get_loading_state()
    {
        if (!_flushed)
        {
            int totalModels = Math.Max(1, _load_total_models);
            int loadedModels = Math.Clamp(_load_total_models - _model_jobs.Count, 0, totalModels);
            return new GDictionary
            {
                ["progress"] = 0.22f + 0.48f * (loadedModels / (float)totalModels),
                ["phase"] = $"正在载入高清模型 {loadedModels}/{_load_total_models}",
                ["complete"] = false,
            };
        }
        if (_anim_pending.Count > 0)
        {
            int totalAnimations = Math.Max(1, _load_total_animations);
            int spawned = Math.Clamp(totalAnimations - _anim_pending.Count, 0, totalAnimations);
            return new GDictionary
            {
                ["progress"] = 0.76f + 0.18f * (spawned / (float)totalAnimations),
                ["phase"] = $"正在建立场景对象 {_load_total_placements - _anim_pending.Count}/{_load_total_placements}",
                ["complete"] = false,
            };
        }
        return new GDictionary
        {
            ["progress"] = 0.95f,
            ["phase"] = "正在预热渲染器",
            ["complete"] = _flushed && _model_jobs.Count == 0 && _queue.Count == 0 && _anim_pending.Count == 0,
        };
    }

    public void enqueue_placement(GodotObject p)
    {
        if (p == null)
            return;
        string id = GS(p, "id");
        if (id is "sloc" or "ncam" or "Start")
            return;
        if (_queue.Count >= MaxStatic)
            return;
        string appearanceId = GS(p, "skin_id");
        if (appearanceId.Length != 4 || ContainsNul(appearanceId))
            appearanceId = id;
        bool isUnit = p.Get("is_unit").AsBool();
        int variation = GI(p, "variation");
        string modelRel = "";
        if (catalog != null)
            modelRel = catalog.Call("resolve_model", appearanceId, isUnit, variation).AsString();
        if (string.IsNullOrEmpty(modelRel))
            modelRel = GS(p, "model_path");
        string absPath = "";
        if (archive != null && !string.IsNullOrEmpty(modelRel))
        {
            absPath = archive.Call("resolve_path", modelRel).AsString();
            if (string.IsNullOrEmpty(absPath))
                absPath = Paths.ResolveGamePath(modelRel);
        }
        if (string.IsNullOrEmpty(absPath))
        {
            _miss++;
            return;
        }
        string replaceable = "";
        if (catalog != null && !isUnit)
            replaceable = catalog.Call("destructable_tex_file", appearanceId).AsString();
        Vector3 pos = GV3(p, "position");
        if (sampler != null)
        {
            float groundY = sampler.Call("sample_y", pos.X, pos.Z).AsSingle();
            if (pos.Y < groundY + 8f)
                pos.Y = groundY;
            if (isUnit && catalog != null)
                pos.Y += catalog.Call("unit_move_height", appearanceId).AsSingle();
        }
        var basis = new Basis(Vector3.Up, -GF(p, "angle"));
        Vector3 scl = p.Get("scale").AsVector3();
        if (scl.Length() <= 0.01f)
            scl = Vector3.One;
        if (catalog != null)
            scl *= catalog.Call("model_scale", appearanceId, isUnit).AsSingle();
        basis = basis.Scaled(scl);
        var xform = new Transform3D(basis, pos);
        var item = new GDictionary
        {
            ["abs_path"] = absPath,
            ["model_rel"] = modelRel,
            ["replaceable"] = replaceable,
            ["xform"] = xform,
            ["center"] = pos,
            ["radius"] = 200.0f,
            ["rawcode"] = id,
            ["skin_rawcode"] = appearanceId,
            ["variation"] = variation,
            ["is_unit"] = isUnit,
            ["player_owner"] = GI(p, "player_owner"),
            ["resolved"] = true,
        };
        _queue.Add(item);
        if (!_model_cache.ContainsKey(absPath) && !_model_jobs.Contains(absPath))
            _model_jobs.Add(absPath);
    }

    private static bool ContainsNul(string value)
    {
        foreach (char c in value)
        {
            if (c == '\0')
                return true;
        }
        return false;
    }

    public void begin_streaming()
    {
        _load_total_models = _model_jobs.Count;
        _load_total_placements = _queue.Count;
        _load_total_animations = 0;
        if (_queue.Count == 0 && _model_jobs.Count == 0)
        {
            _flushed = true;
            return;
        }
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        int loaded = 0;
        while (loaded < BatchLoadPerFrame && _model_jobs.Count > 0)
        {
            string next = _model_jobs[0];
            _model_jobs.RemoveAt(0);
            EnsureModel(next);
            loaded++;
        }
        if (!_flushed && _model_jobs.Count == 0 && _queue.Count > 0)
        {
            FlushQueueIntoBatches();
            _flushed = true;
            GD.Print($"W3MdxBatcher: ok={_ok} miss={_miss} fail={_fail} batches={_batches.Count}");
            UpdateFrustumVisibility();
            Node world = GetParent();
            if (world != null && world.HasMethod("_refresh_status"))
                world.Call("_refresh_status");
        }
        if (_flushed)
        {
            bool hadPending = _anim_pending.Count > 0;
            int spawned = 0;
            while (spawned < AnimSpawnPerFrame && _anim_pending.Count > 0)
            {
                GDictionary item = _anim_pending[0].AsGodotDictionary();
                _anim_pending.RemoveAt(0);
                string absPath = item["abs_path"].AsString();
                GodotObject model = _model_cache.ContainsKey(absPath) ? _model_cache[absPath].AsGodotObject() : null;
                if (model != null && GA(model, "geosets").Count > 0)
                    SpawnAnimated(item, model);
                spawned++;
            }
            if (hadPending && _anim_pending.Count == 0)
            {
                GD.Print($"W3MdxBatcher: animated={_anim_instances.Count} active={_anim_active_n} near<={AnimNearDist:F0} far<={AnimFarDist:F0} draw<={AnimDrawDist:F0}");
                Node world = GetParent();
                if (world != null && world.HasMethod("_refresh_status"))
                    world.Call("_refresh_status");
            }
            _proximity_timer += (float)delta;
            if (_proximity_timer >= ProximityCheckSec)
            {
                _proximity_timer = 0f;
                UpdateAnimProximity();
            }
            _frustum_timer += (float)delta;
            if (_frustum_timer >= FrustumCheckSec)
            {
                _frustum_timer = 0f;
                UpdateFrustumVisibility();
            }
        }
    }

    private void EnsureModel(string absPath)
    {
        if (_model_cache.ContainsKey(absPath))
            return;
        GodotObject model = LoadModelFile(absPath);
        _model_cache[absPath] = model;
        if (model == null || GA(model, "geosets").Count == 0)
            _fail++;
    }

    private static GodotObject LoadModelFile(string absPath) => W3MdxModel.LoadFile(absPath);

    private void SpawnAnimated(GDictionary item, GodotObject model)
    {
        if (_anim_root == null || !IsInstanceValid(_anim_root))
        {
            _anim_root = new Node3D { Name = "AnimatedPreviewUnits" };
            AddChild(_anim_root);
        }
        var inst = new W3MdxInstance();
        _anim_root.AddChild(inst);
        inst.Transform = item["xform"].AsTransform3D();
        inst.setup(model, textures, item["abs_path"].AsString(), new GDictionary
        {
            ["animate"] = true,
            ["proximity_gate"] = true,
            ["team_color"] = item.ContainsKey("player_owner") ? item["player_owner"].AsInt32() : 0,
        });
        inst.set_anim_active(false);
        inst.SetMeta("w3_preview_item", item);
        bool isUnit = item.ContainsKey("is_unit") && item["is_unit"].AsBool();
        if (isUnit)
            inst.SetMeta("w3_selection_body", AddUnitSelectionBody(inst, Transform3D.Identity, model, item));
        else
            inst.SetMeta("w3_selection_body", Variant.From((GodotObject)null));
        inst.Visible = false;
        _anim_instances.Add(inst);
        _ok++;
    }

    private void UpdateAnimProximity()
    {
        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null)
            return;
        Vector3 camPos = cam.GlobalPosition;
        Godot.Collections.Array<Plane> planes = cam.GetFrustum();
        float near2 = AnimNearDist * AnimNearDist;
        float far2 = AnimFarDist * AnimFarDist;
        float draw2 = AnimDrawDist * AnimDrawDist;
        int cameraLod = CameraLodLevel(cam);
        var alive = new GArray();
        var scored = new List<(GodotObject inst, float d2)>();
        foreach (Variant instV in _anim_instances)
        {
            GodotObject inst = instV.AsGodotObject();
            if (inst == null || !IsInstanceValid(inst))
                continue;
            alive.Add(inst);
            float d2 = ((Node3D)inst).GlobalPosition.DistanceSquaredTo(camPos);
            scored.Add((inst, d2));
        }
        _anim_instances = alive;
        scored.Sort((a, b) => a.d2.CompareTo(b.d2));

        _anim_active_n = 0;
        int animationSlots = 0;
        foreach ((GodotObject inst, float d2) in scored)
        {
            var node3d = (Node3D)inst;
            GDictionary itemMeta = inst.HasMeta("w3_preview_item") ? inst.GetMeta("w3_preview_item").AsGodotDictionary() : new GDictionary();
            float cullRadius = (itemMeta.ContainsKey("radius") ? itemMeta["radius"].AsSingle() : 200f) + FrustumMargin;
            bool draw = cameraLod < 2 && d2 <= draw2 && SphereInFrustum(node3d.GlobalPosition, cullRadius, planes);
            node3d.Visible = draw;
            Variant selectionBodyVariant = inst.HasMeta("w3_selection_body") ? inst.GetMeta("w3_selection_body") : default;
            SetUnitSelectionEnabled(selectionBodyVariant.AsGodotObject(), draw);
            bool wasActive = inst.Call("is_anim_active").AsBool();
            bool animate = false;
            if (draw && animationSlots < MaxNearAnims)
                animate = d2 <= (wasActive ? far2 : near2);
            if (animate)
            {
                animationSlots++;
                _anim_active_n++;
            }
            inst.Call("set_anim_active", animate);
        }
    }

    private void FlushQueueIntoBatches()
    {
        var byGkey = new GDictionary();
        var placementCounts = new Dictionary<string, int>();
        foreach (Variant itemV in _queue)
        {
            string p = itemV.AsGodotDictionary()["abs_path"].AsString();
            placementCounts[p] = placementCounts.GetValueOrDefault(p, 0) + 1;
        }
        foreach (Variant itemV in _queue)
        {
            GDictionary item = itemV.AsGodotDictionary();
            string absPath = item["abs_path"].AsString();
            GodotObject model = _model_cache.ContainsKey(absPath) ? _model_cache[absPath].AsGodotObject() : null;
            if (model == null || GA(model, "geosets").Count == 0)
                continue;
            ApplyModelRadius(item, model);
            if (NeedsLiveInstance(item, model, placementCounts.GetValueOrDefault(absPath, 0)))
            {
                _anim_pending.Add(item);
                continue;
            }
            bool isUnit = item.ContainsKey("is_unit") && item["is_unit"].AsBool();
            if (isUnit)
                AddUnitSelectionBody(GetSelectableUnitsRoot(), item["xform"].AsTransform3D(), model, item);
            string replaceable = item["replaceable"].AsString();
            string bankKey = $"{absPath}|{replaceable}";
            if (!_mesh_cache.ContainsKey(bankKey))
                _mesh_cache[bankKey] = _build_geoset_meshes(model, replaceable);
            GArray geos = _mesh_cache[bankKey].AsGodotArray();
            _ok++;
            for (int gi = 0; gi < geos.Count; gi++)
            {
                string gkey = $"{bankKey}#{gi}";
                if (!byGkey.ContainsKey(gkey))
                    byGkey[gkey] = new GDictionary { ["geo"] = geos[gi], ["items"] = new GArray() };
                byGkey[gkey].AsGodotDictionary()["items"].AsGodotArray().Add(item);
            }
        }
        _queue.Clear();
        _load_total_animations = _anim_pending.Count;

        foreach (Variant keyV in byGkey.Keys)
        {
            string gkey = keyV.AsString();
            GDictionary pack = byGkey[gkey].AsGodotDictionary();
            GDictionary geo = pack["geo"].AsGodotDictionary();
            GArray items = pack["items"].AsGodotArray();
            if (items.Count == 0)
                continue;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = geo["mesh"].AsGodotObject() as Mesh,
                InstanceCount = items.Count,
            };
            var centers = new Vector3[items.Count];
            var radii = new float[items.Count];
            var xforms = new Godot.Collections.Array<Transform3D>();
            for (int i = 0; i < items.Count; i++)
            {
                GDictionary it = items[i].AsGodotDictionary();
                Transform3D xf = it["xform"].AsTransform3D();
                mm.SetInstanceTransform(i, xf);
                centers[i] = it["center"].AsVector3();
                radii[i] = it["radius"].AsSingle();
                xforms.Add(xf);
            }
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = geo["mat"].AsGodotObject() as Material,
                CastShadow = (geo.ContainsKey("casts_shadow") ? geo["casts_shadow"].AsBool() : true)
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Static,
            };
            var aabb = new Aabb(centers[0], Vector3.Zero);
            float maxRadius = 0f;
            for (int i = 0; i < centers.Length; i++)
            {
                aabb = aabb.Expand(centers[i]);
                maxRadius = Mathf.Max(maxRadius, radii[i]);
            }
            mmi.CustomAabb = aabb.Grow(maxRadius + 128f);
            mmi.ExtraCullMargin = 128f;
            mmi.VisibilityRangeEndMargin = 400f;
            mmi.VisibilityRangeEnd = PropDrawDist + 800f;
            AddChild(mmi);
            _batches[gkey] = new GDictionary
            {
                ["mm"] = mm,
                ["lod_meshes"] = geo.ContainsKey("lod_meshes") ? geo["lod_meshes"] : new GDictionary(),
                ["centers"] = centers,
                ["radii"] = radii,
                ["xforms"] = xforms,
                ["items"] = items,
            };
        }
    }

    // 用缩放后 AABB 对角线的一半替换占位剔除半径。
    private void ApplyModelRadius(GDictionary item, GodotObject model)
    {
        string absPath = item["abs_path"].AsString();
        if (!_model_radius_cache.ContainsKey(absPath))
            _model_radius_cache[absPath] = ModelLocalAabb(model).Size.Length() * 0.5f;
        Transform3D xf = item["xform"].AsTransform3D();
        Vector3 s = xf.Basis.Scale;
        item["radius"] = _model_radius_cache[absPath].AsSingle() * Mathf.Max(s.X, Mathf.Max(s.Y, s.Z));
    }

    private bool NeedsLiveInstance(GDictionary item, GodotObject model, int placementCount)
    {
        if (GA(model, "sequences").Count == 0)
            return false;
        bool isUnit = item.ContainsKey("is_unit") && item["is_unit"].AsBool();
        if (isUnit)
        {
            return GA(model, "bones").Count > 0
                || model.Call("has_idle_layer_animation").AsBool()
                || GA(model, "particle_emitters2").Count > 0
                || GA(model, "lights").Count > 0;
        }
        string modelRel = item["model_rel"].AsString().Replace("\\", "/").ToLowerInvariant();
        bool isAmbientActor = modelRel.Contains("/bird/") || modelRel.Contains("/fish/")
            || modelRel.Contains("fountain") || modelRel.Contains("dolphin")
            || modelRel.Contains("whale") || modelRel.Contains("lilypad") || modelRel.Contains("lillypad");
        if (placementCount <= 64 && isAmbientActor)
            return model.Call("has_idle_bone_motion").AsBool() || model.Call("has_idle_layer_animation").AsBool();
        return placementCount <= 16 && (GA(model, "particle_emitters2").Count > 0 || GA(model, "lights").Count > 0);
    }

    public GArray _build_geoset_meshes(GodotObject model, string replaceableTex)
    {
        var outArr = new GArray();
        int standTime = 0;
        int standIndex = model.Call("find_idle_sequence").AsInt32();
        GArray sequences = GA(model, "sequences");
        if (standIndex >= 0 && standIndex < sequences.Count)
            standTime = GI(sequences[standIndex].AsGodotObject(), "interval_start");
        var byLod = new Dictionary<int, List<(GodotObject g, int gi)>>();
        GArray geosets = GA(model, "geosets");
        for (int gi = 0; gi < geosets.Count; gi++)
        {
            GodotObject g = geosets[gi].AsGodotObject();
            int lod = Math.Max(0, GI(g, "lod"));
            if (model.Call("geoset_alpha_at", gi, standTime).AsSingle() <= 0.01f)
                continue;
            if (!byLod.TryGetValue(lod, out List<(GodotObject, int)> list))
            {
                list = new List<(GodotObject, int)>();
                byLod[lod] = list;
            }
            list.Add((g, gi));
        }
        List<(GodotObject g, int gi)> baseGeosets = byLod.TryGetValue(0, out List<(GodotObject, int)> v0) ? v0 : new List<(GodotObject, int)>();
        if (baseGeosets.Count == 0 && byLod.Count > 0)
        {
            int firstLod = int.MaxValue;
            foreach (int k in byLod.Keys)
                firstLod = Math.Min(firstLod, k);
            baseGeosets = byLod[firstLod];
        }
        for (int surfaceIndex = 0; surfaceIndex < baseGeosets.Count; surfaceIndex++)
        {
            GodotObject baseG = baseGeosets[surfaceIndex].g;
            var lodMeshes = new GDictionary();
            foreach (KeyValuePair<int, List<(GodotObject g, int gi)>> pair in byLod)
            {
                List<(GodotObject g, int gi)> lodGeosets = pair.Value;
                if (surfaceIndex >= lodGeosets.Count)
                    continue;
                GodotObject lodG = lodGeosets[surfaceIndex].g;
                if (GI(lodG, "material_id") != GI(baseG, "material_id"))
                    continue;
                lodMeshes[pair.Key] = CommitGeoset(lodG);
            }
            ArrayMesh baseMesh = lodMeshes.ContainsKey(0) ? lodMeshes[0].AsGodotObject() as ArrayMesh : null;
            baseMesh ??= CommitGeoset(baseG);
            outArr.Add(new GDictionary
            {
                ["mesh"] = baseMesh,
                ["lod_meshes"] = lodMeshes,
                ["mat"] = MakeMaterial(model, baseG, replaceableTex),
                ["casts_shadow"] = GeosetCastsShadow(model, baseG),
            });
        }
        return outArr;
    }

    // WC3 滤镜模式 0/1（不透明/镂空）写深度并投射阴影；2 及以上不写深度，
    // 加色/调制图层若投影会在阴影贴图里画出实心方块（单独的 blob shadow 系统处理地面阴影）。
    private static bool GeosetCastsShadow(GodotObject model, GodotObject g)
    {
        GArray materials = GA(model, "materials");
        if (materials.Count == 0)
            return true;
        int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
        GArray layers = materials[mid].AsGodotArray();
        if (layers.Count == 0)
            return true;
        return GI(layers[0].AsGodotObject(), "filter_mode") <= 1;
    }

    private static ArrayMesh CommitGeoset(GodotObject g)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] verts = GV3A(g, "vertices");
        Vector3[] norms = GV3A(g, "normals");
        Vector2[] uvs = GV2A(g, "uvs");
        int[] faces = GI32A(g, "faces");
        float[] tangents = GF32A(g, "tangents");
        int nvert = verts.Length;
        for (int fi = 0; fi + 2 < faces.Length; fi += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int idx = faces[fi + k];
                if (idx < 0 || idx >= nvert)
                    continue;
                st.SetNormal(idx < norms.Length ? norms[idx] : Vector3.Up);
                if (tangents.Length >= (idx + 1) * 4)
                {
                    int ti = idx * 4;
                    st.SetTangent(new Plane(tangents[ti], tangents[ti + 1], tangents[ti + 2], tangents[ti + 3]));
                }
                if (idx < uvs.Length)
                    st.SetUV(uvs[idx]);
                st.AddVertex(verts[idx]);
            }
        }
        return st.Commit();
    }

    private Material MakeMaterial(GodotObject model, GodotObject g, string replaceableTex)
    {
        GArray materials = GA(model, "materials");
        if (materials.Count > 0)
        {
            int hdMid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
            GArray hdLayers = materials[hdMid].AsGodotArray();
            if (hdLayers.Count > 0 && GI(hdLayers[0].AsGodotObject(), "shader") == 1)
                return MakeHdMaterial(model, hdLayers[0].AsGodotObject(), replaceableTex);
        }
        string texPath = ResolveTex(model, g, replaceableTex);
        GodotObject? layer = null;
        if (materials.Count > 0)
        {
            int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
            GArray layers = materials[mid].AsGodotArray();
            if (layers.Count > 0) layer = layers[0].AsGodotObject();
        }
        Texture2D? texture = !string.IsNullOrEmpty(texPath) && textures != null
            ? textures.Call("get_texture", texPath).As<Texture2D>() : null;
        return W3ModelMaterials.CreateClassic(texture, texPath,
            layer != null ? GI(layer, "filter_mode") : 0,
            layer != null ? GI(layer, "flags") : 0,
            layer != null ? GF(layer, "alpha") : 1f);
    }

    private ShaderMaterial MakeHdMaterial(GodotObject model, GodotObject layer, string replaceableTex)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(HdShaderPath(GI(layer, "filter_mode"))) };
        int[] ids = GI32A(layer, "texture_ids");
        string[] uniformNames = { "diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map" };
        for (int slot = 0; slot < Math.Min(6, ids.Length); slot++)
        {
            string path = ResolveHdTex(model, ids[slot], slot, replaceableTex);
            if (string.IsNullOrEmpty(path) || textures == null)
                continue;
            material.SetShaderParameter(uniformNames[slot], textures.Call("get_texture", path).As<Texture2D>());
        }
        material.SetShaderParameter("layer_alpha", Mathf.Clamp(GF(layer, "alpha"), 0f, 1f));
        material.SetShaderParameter("filter_mode", GI(layer, "filter_mode"));
        material.SetShaderParameter("emissive_gain", Mathf.Max(0f, GF(layer, "emissive_gain")));
        material.SetShaderParameter("unshaded_layer", (GI(layer, "flags") & 0x1) != 0);
        return material;
    }

    private static string HdShaderPath(int filterMode)
    {
        if (filterMode <= 1)
            return "res://addons/w3kit/shaders/w3_reforged_unit_opaque.gdshader";
        if (filterMode == 3 || filterMode == 4)
            return "res://addons/w3kit/shaders/w3_reforged_unit_add.gdshader";
        if (filterMode == 5 || filterMode == 6)
            return "res://addons/w3kit/shaders/w3_reforged_unit_modulate.gdshader";
        return "res://addons/w3kit/shaders/w3_reforged_unit.gdshader";
    }

    private static readonly Dictionary<int, string> ReplaceableStems = new()
    {
        [1] = "ReplaceableTextures/TeamColor/TeamColor00",
        [2] = "ReplaceableTextures/TeamGlow/TeamGlow00",
        [11] = "ReplaceableTextures/Cliff/Cliff0",
        [31] = "ReplaceableTextures/LordaeronTree/LordaeronSummerTree",
        [32] = "ReplaceableTextures/AshenvaleTree/AshenTree",
        [33] = "ReplaceableTextures/BarrensTree/BarrensTree",
        [34] = "ReplaceableTextures/NorthrendTree/NorthTree",
        [35] = "ReplaceableTextures/Mushroom/MushroomTree",
        [36] = "ReplaceableTextures/RuinsTree/RuinsTree",
        [37] = "ReplaceableTextures/OutlandMushroomTree/MushroomTree",
    };

    private string ResolveHdTex(GodotObject model, int texIndex, int pbrSlot, string replaceableTex)
    {
        GArray textureList = GA(model, "textures");
        if (texIndex < 0 || texIndex >= textureList.Count)
            return "";
        string authored = textureList[texIndex].AsString();
        if (!string.IsNullOrEmpty(authored))
            return authored.GetBaseName() + ".dds";
        int[] replaceableIds = GI32A(model, "texture_replaceable_ids");
        int rid = texIndex < replaceableIds.Length ? replaceableIds[texIndex] : 0;
        if (rid == 1 || rid == 2)
        {
            string teamStem = rid == 1 ? "ReplaceableTextures/TeamColor/TeamColor" : "ReplaceableTextures/TeamGlow/TeamGlow";
            return teamStem + "00.dds";
        }
        string stem = "";
        if (!string.IsNullOrEmpty(replaceableTex) && rid >= 31)
            stem = replaceableTex.Replace("\\", "/").GetBaseName();
        else if (ReplaceableStems.ContainsKey(rid))
            stem = ReplaceableStems[rid];
        if (string.IsNullOrEmpty(stem))
            return "Textures/White.dds";
        return stem + HdSlotSuffix(pbrSlot) + ".dds";
    }

    private static string HdSlotSuffix(int pbrSlot) => pbrSlot switch
    {
        0 => "_diffuse",
        1 => "_normal",
        2 => "_orm",
        3 => "_emissive",
        _ => "",
    };

    private string ResolveTex(GodotObject model, GodotObject g, string replaceableTex)
    {
        GArray materials = GA(model, "materials");
        if (materials.Count == 0)
            return "";
        int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
        GArray layers = materials[mid].AsGodotArray();
        if (layers.Count == 0)
            return "";
        int tid = GI(layers[0].AsGodotObject(), "texture_id");
        GArray textureList = GA(model, "textures");
        if (tid < 0 || tid >= textureList.Count)
            return "";
        string path = textureList[tid].AsString();
        if (!string.IsNullOrEmpty(path))
            return path;
        int[] replaceableIds = GI32A(model, "texture_replaceable_ids");
        int rid = tid < replaceableIds.Length ? replaceableIds[tid] : 0;
        if (!string.IsNullOrEmpty(replaceableTex) && rid >= 31)
        {
            string rt = replaceableTex.Replace("\\", "/");
            if (!rt.ToLowerInvariant().EndsWith(".blp"))
                rt += ".blp";
            return rt;
        }
        return ReplaceableStems.TryGetValue(rid, out string stem) ? stem + ".blp" : "";
    }

    private void UpdateFrustumVisibility()
    {
        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null || _batches.Count == 0)
            return;
        Vector3 camPos = cam.GlobalPosition;
        Godot.Collections.Array<Plane> planes = cam.GetFrustum();
        UpdateBatchLod(cam);
        float draw2 = PropDrawDist * PropDrawDist;
        _visible_static = 0;
        foreach (Variant keyV in _batches.Keys)
        {
            GDictionary b = _batches[keyV].AsGodotDictionary();
            var mm = b["mm"].AsGodotObject() as MultiMesh;
            var centers = (Vector3[])b["centers"];
            var radii = (float[])b["radii"];
            var xforms = (Godot.Collections.Array<Transform3D>)b["xforms"];
            GArray items = b["items"].AsGodotArray();
            int vis = 0;
            for (int i = 0; i < centers.Length; i++)
            {
                bool visible = camPos.DistanceSquaredTo(centers[i]) <= draw2
                    && SphereInFrustum(centers[i], radii[i] + FrustumMargin, planes);
                if (visible)
                {
                    mm.SetInstanceTransform(vis, xforms[i]);
                    vis++;
                }
                GDictionary item = items[i].AsGodotDictionary();
                if (item.ContainsKey("is_unit") && item["is_unit"].AsBool())
                    SetUnitSelectionEnabled(item.ContainsKey("selection_body") ? item["selection_body"].AsGodotObject() : null, visible);
            }
            mm.VisibleInstanceCount = vis;
            _visible_static += vis;
        }
    }

    private int CameraLodLevel(Camera3D cam)
    {
        if (cam.Projection == Camera3D.ProjectionType.Orthogonal)
        {
            if (cam.Size >= 2600f) return 3;
            if (cam.Size >= 1500f) return 2;
            if (cam.Size >= 800f) return 1;
            return 0;
        }
        float groundY = 0f;
        if (sampler != null)
            groundY = sampler.Call("sample_y", cam.GlobalPosition.X, cam.GlobalPosition.Z).AsSingle();
        float height = Mathf.Max(0f, cam.GlobalPosition.Y - groundY);
        if (height >= 2800f) return 3;
        if (height >= 1600f) return 2;
        if (height >= 850f) return 1;
        return 0;
    }

    private void UpdateBatchLod(Camera3D cam)
    {
        int nextLod = CameraLodLevel(cam);
        if (nextLod == _lod_level)
            return;
        _lod_level = nextLod;
        foreach (Variant batchV in _batches.Values)
        {
            GDictionary batch = batchV.AsGodotDictionary();
            GDictionary lodMeshes = batch["lod_meshes"].AsGodotDictionary();
            if (lodMeshes.Count == 0)
                continue;
            int selectedLod = _lod_level;
            while (selectedLod > 0 && !lodMeshes.ContainsKey(selectedLod))
                selectedLod--;
            if (!lodMeshes.ContainsKey(selectedLod))
            {
                int minKey = int.MaxValue;
                foreach (Variant k in lodMeshes.Keys)
                    minKey = Math.Min(minKey, k.AsInt32());
                selectedLod = minKey;
            }
            var mm = batch["mm"].AsGodotObject() as MultiMesh;
            mm.Mesh = lodMeshes[selectedLod].AsGodotObject() as Mesh;
        }
    }

    private Node3D GetSelectableUnitsRoot()
    {
        if (_selectable_units_root != null && IsInstanceValid(_selectable_units_root))
            return _selectable_units_root;
        _selectable_units_root = new Node3D { Name = "SelectablePreviewUnits" };
        AddChild(_selectable_units_root);
        return _selectable_units_root;
    }

    private StaticBody3D AddUnitSelectionBody(Node3D parent, Transform3D localXform, GodotObject model, GDictionary item)
    {
        if (parent == null)
            return null;
        var body = new StaticBody3D
        {
            Name = $"SelectableUnit_{(item.ContainsKey("rawcode") ? item["rawcode"].AsString() : "unit")}",
            Transform = localXform,
            CollisionLayer = 4,
            CollisionMask = 0,
        };
        body.AddToGroup("enemy");
        body.AddToGroup("w3_selectable_unit");
        body.SetMeta("w3_preview_item", item);
        parent.AddChild(body);

        Aabb aabb = ModelLocalAabb(model);
        var shape = new BoxShape3D { Size = aabb.Size };
        var collision = new CollisionShape3D { Name = "SelectionCollision", Shape = shape, Position = aabb.GetCenter(), Disabled = true };
        body.AddChild(collision);
        body.SetMeta("w3_selection_collision", collision);
        body.SetMeta("w3_selection_radius", Mathf.Max(aabb.Size.X, aabb.Size.Z) * 0.55f);
        item["selection_body"] = body;
        return body;
    }

    private static void SetUnitSelectionEnabled(GodotObject body, bool enabled)
    {
        if (body is not StaticBody3D staticBody || !IsInstanceValid(staticBody))
            return;
        var collision = staticBody.HasMeta("w3_selection_collision") ? staticBody.GetMeta("w3_selection_collision").AsGodotObject() as CollisionShape3D : null;
        if (collision != null && IsInstanceValid(collision))
            collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, !enabled);
    }

    private static Aabb ModelLocalAabb(GodotObject model)
    {
        bool hasVertex = false;
        Vector3 minV = Vector3.Zero;
        Vector3 maxV = Vector3.Zero;
        foreach (Variant geosetV in GA(model, "geosets"))
        {
            GodotObject geoset = geosetV.AsGodotObject();
            int lod = GI(geoset, "lod");
            if (lod != 0 && lod != -1)
                continue;
            foreach (Vector3 vertex in GV3A(geoset, "vertices"))
            {
                if (!hasVertex)
                {
                    minV = vertex;
                    maxV = vertex;
                    hasVertex = true;
                }
                else
                {
                    minV = minV.Min(vertex);
                    maxV = maxV.Max(vertex);
                }
            }
        }
        if (!hasVertex)
            return new Aabb(new Vector3(-64f, 0f, -64f), new Vector3(128f, 128f, 128f));
        Vector3 size = maxV - minV;
        size.X = Mathf.Max(size.X, 24f);
        size.Y = Mathf.Max(size.Y, 24f);
        size.Z = Mathf.Max(size.Z, 24f);
        return new Aabb(minV, size);
    }

    // Camera3D.GetFrustum() 平面法线朝外（is_point_over == 在外侧），
    // 球心到任一面的距离超过半径即被剔除。
    private static bool SphereInFrustum(Vector3 center, float radius, Godot.Collections.Array<Plane> planes)
    {
        foreach (Plane plane in planes)
        {
            if (plane.DistanceTo(center) > radius)
                return false;
        }
        return true;
    }
}
