using System;
using System.Collections.Generic;
using Godot;
using Diablo1Like.W3.Paths;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// MDX 实例渲染器；迁移自 addons/w3kit/w3_mdx_instance.gd。
/// model 现为 C# W3MdxModel（见 W3MdxModel.cs），仍通过 Get/Call 鸭子类型访问
/// （与旧 GDS 门面字段名保持一致），驱动 MeshInstance3D、播放 Stand/Walk 动画、
/// GPU 蒙皮骨架、粒子/灯光特效，蒙皮计算走 CPU（Warsmash 移植）。
/// 部分 tools/*.gd 探针仍 `preload(".../W3MdxInstance.cs").new()` 直接构造实例，
/// 因此 GDScript 可调用的 public 成员保持 snake_case 命名。
/// </summary>
[GlobalClass]
public partial class W3MdxInstance : Node3D
{
    // ???????key -> Array[Dictionary{mesh,mat,geoset_id,casts_shadow}]
    // ? GDScript ?????"?????"????????
    private static readonly GDictionary _mesh_bank = new();

    private const int SkinEveryNFrames = 2;

    public GodotObject model;
    public GodotObject textures;
    public int _seq_index = -1;
    public float _anim_time;
    public float _global_time;
    public bool _playing = true;
    public bool _loop = true;
    public bool _playback;
    public bool _animate;
    public bool _proximity_gate;
    public bool _anim_active = true;
    public int _skin_frame;
    public bool _cliff_remap;
    // Optional per-instance geometry transform, after axis remapping. Transformed
    // geometry must bypass the shared mesh cache. Terrain rules live in the caller.
    internal Func<Vector3, Vector3, (Vector3 Position, Vector3 Normal)>? StaticVertexTransform;
    public string _replaceable_tex = "";
    public Node3D _mesh_root;
    public MeshInstance3D _placeholder;
    public string _cache_key = "";
    public GArray _geoset_nodes = new();
    public GArray _bind_geosets = new();
    public GArray _skinned_arrays = new();
    public GArray _particle_nodes = new();
    public GArray _light_nodes = new();
    public Skeleton3D _skeleton;
    public Skin _skin;
    public GDictionary _bone_index_by_oid = new();
    public GArray _material_tracks = new();

    public static void clear_mesh_bank() => _mesh_bank.Clear();

    // ---- 小工具：model/geoset/bone 现为 C# W3MdxModel（RefCounted），
    // 仍统一走 Get()/Call() 鸭子类型访问 ----------
    private static GArray GA(GodotObject o, string p) => o.Get(p).AsGodotArray();
    private static string GS(GodotObject o, string p) => o.Get(p).AsString();
    private static int GI(GodotObject o, string p) => o.Get(p).AsInt32();
    private static float GF(GodotObject o, string p) => o.Get(p).AsSingle();
    private static Vector3 GV3(GodotObject o, string p) => o.Get(p).AsVector3();
    private static Vector3[] GV3A(GodotObject o, string p) => o.Get(p).AsVector3Array();
    private static Vector2[] GV2A(GodotObject o, string p) => o.Get(p).AsVector2Array();
    private static int[] GI32A(GodotObject o, string p) => o.Get(p).AsInt32Array();
    private static float[] GF32A(GodotObject o, string p) => o.Get(p).AsFloat32Array();
    private static Color GCol(GodotObject o, string p) => o.Get(p).AsColor();
    private static Color[] GColA(GodotObject o, string p) => o.Get(p).AsColorArray();

    private static bool OB(GDictionary o, string k, bool def = false) => o.ContainsKey(k) ? o[k].AsBool() : def;
    private static int OI(GDictionary o, string k, int def = 0) => o.ContainsKey(k) ? o[k].AsInt32() : def;
    private static string OStr(GDictionary o, string k, string def = "") => o.ContainsKey(k) ? o[k].AsString() : def;

    // ---- setup --------------------------------------------------------

    public void setup(GodotObject pModel, GodotObject pTextures, string cacheKey = "", GDictionary opts = null)
    {
        opts ??= new GDictionary();
        model = pModel;
        textures = pTextures;
        _cache_key = cacheKey;
        bool wantAnimate = OB(opts, "animate");
        _playback = wantAnimate && model != null && (
            GA(model, "sequences").Count > 0
            || model.Call("has_layer_animation").AsBool()
            || GA(model, "particle_emitters2").Count > 0
            || GA(model, "lights").Count > 0
        );
        _animate = _playback && model != null && GA(model, "bones").Count > 0;
        _proximity_gate = OB(opts, "proximity_gate") && _playback;
        _cliff_remap = OB(opts, "cliff_remap");
        _replaceable_tex = OStr(opts, "replaceable_texture");
        _geoset_nodes.Clear();
        _bind_geosets.Clear();
        _skinned_arrays.Clear();
        _particle_nodes.Clear();
        _light_nodes.Clear();
        _skeleton = null;
        _skin = null;
        _bone_index_by_oid.Clear();
        _material_tracks.Clear();
        _anim_active = !_proximity_gate;
        _skin_frame = 0;
        _anim_time = 0f;
        _global_time = 0f;
        foreach (Node c in GetChildren())
            c.QueueFree();
        _mesh_root = new Node3D { Name = "MdxMeshes" };
        AddChild(_mesh_root);
        if (model == null || GA(model, "geosets").Count == 0)
        {
            AddPlaceholder();
            return;
        }
        if (_animate)
        {
            BuildAnimatedMeshes(opts);
        }
        else
        {
            if (StaticVertexTransform != null)
            {
                BuildMeshes(opts);
            }
            else
            {
                string bankKey = cacheKey;
                if (opts.ContainsKey("cliff_texture"))
                    bankKey = $"{bankKey}|cliff:{OStr(opts, "cliff_texture")}";
                if (opts.ContainsKey("team_color"))
                    bankKey = $"{bankKey}|tc:{OI(opts, "team_color")}";
                if (!string.IsNullOrEmpty(_replaceable_tex))
                    bankKey = $"{bankKey}|rtex:{_replaceable_tex}";
                if (_cliff_remap)
                    bankKey = $"{bankKey}|cremap";
                if (!_playback && !string.IsNullOrEmpty(bankKey) && _mesh_bank.ContainsKey(bankKey))
                {
                    AttachBank(_mesh_bank[bankKey].AsGodotArray());
                }
                else
                {
                    GArray bank = BuildMeshes(opts);
                    if (!_playback && !string.IsNullOrEmpty(bankKey))
                        _mesh_bank[bankKey] = bank;
                }
            }
        }
        BindMaterialTracks(opts);
        BuildModelEffects(opts);
        play_idle();
        if (_proximity_gate)
            set_anim_active(false);
    }

    public void set_anim_active(bool on)
    {
        if (!_playback)
            return;
        if (on == _anim_active)
        {
            SetProcess(on);
            return;
        }
        _anim_active = on;
        SetProcess(on);
        if (on)
        {
            _update_material_tracks();
            if (_animate)
                SkinAtCurrentTime();
        }
        else if (_animate)
        {
            RestoreBindPose();
        }
        SetEffectsActive(on);
    }

    public bool is_anim_active() => _anim_active;

    public string get_current_sequence_name()
    {
        if (model == null || _seq_index < 0)
            return "";
        GArray sequences = GA(model, "sequences");
        if (_seq_index >= sequences.Count)
            return "";
        return GS(sequences[_seq_index].AsGodotObject(), "name");
    }

    private void RestoreBindPose()
    {
        if (_skeleton != null && IsInstanceValid(_skeleton))
        {
            for (int boneIndex = 0; boneIndex < _skeleton.GetBoneCount(); boneIndex++)
            {
                _skeleton.SetBonePosePosition(boneIndex, Vector3.Zero);
                _skeleton.SetBonePoseRotation(boneIndex, Quaternion.Identity);
                _skeleton.SetBonePoseScale(boneIndex, Vector3.One);
            }
        }
        foreach (Variant miV in _geoset_nodes)
        {
            var mi = miV.AsGodotObject() as MeshInstance3D;
            if (mi == null || !IsInstanceValid(mi))
                continue;
            int gid = mi.HasMeta("geoset_id") ? mi.GetMeta("geoset_id").AsInt32() : -1;
            if (gid >= 0 && gid < _skinned_arrays.Count && _skinned_arrays[gid].AsGodotObject() != null)
                mi.Mesh = _skinned_arrays[gid].AsGodotObject() as Mesh;
        }
        ApplyGeosetVisibility();
    }

    private void AddPlaceholder()
    {
        _placeholder = new MeshInstance3D();
        var box = new BoxMesh { Size = new Vector3(32, 64, 32) };
        _placeholder.Mesh = box;
        _placeholder.Position = new Vector3(0, 32, 0);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.55f, 0.28f) };
        W3ModelMaterials.ApplyClassicDefaults(mat);
        _placeholder.MaterialOverride = mat;
        AddChild(_placeholder);
    }

    private void AttachBank(GArray bank)
    {
        foreach (Variant entryV in bank)
        {
            GDictionary entry = entryV.AsGodotDictionary();
            var mi = new MeshInstance3D
            {
                Mesh = entry["mesh"].AsGodotObject() as Mesh,
                MaterialOverride = entry["mat"].AsGodotObject() as Material,
            };
            mi.SetMeta("geoset_id", entry.ContainsKey("geoset_id") ? entry["geoset_id"].AsInt32() : -1);
            if (!(entry.ContainsKey("casts_shadow") ? entry["casts_shadow"].AsBool() : true))
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _mesh_root.AddChild(mi);
            _geoset_nodes.Add(mi);
        }
    }

    // WC3 ???? 0?????/1??????????????
    // 2 ??????/??/?????????????????????????
    private bool GeosetCastsShadow(GodotObject g)
    {
        if (model == null || GA(model, "materials").Count == 0)
            return true;
        GArray materials = GA(model, "materials");
        int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
        GArray layers = materials[mid].AsGodotArray();
        if (layers.Count == 0)
            return true;
        return GI(layers[0].AsGodotObject(), "filter_mode") <= 1;
    }

    private Material MakeMaterial(GodotObject g, int teamColor, string cliffTexOverride)
    {
        string texPath = TextureForGeoset(g, teamColor, cliffTexOverride);
        if (_cliff_remap)
        {
            var cliffMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/w3kit/shaders/w3_cliff_lit.gdshader") };
            if (!string.IsNullOrEmpty(texPath) && textures != null)
                cliffMat.SetShaderParameter("albedo_tex", textures.Call("get_texture", texPath).As<Texture2D>());
            return cliffMat;
        }
        GArray layers = new();
        if (GA(model, "materials").Count > 0)
        {
            GArray materials = GA(model, "materials");
            int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
            layers = materials[mid].AsGodotArray();
        }
        if (layers.Count == 0)
            return MakeLayerMaterial(null, texPath, 0);
        GodotObject layer0 = layers[0].AsGodotObject();
        if (GI(layer0, "shader") == 1)
            return MakeHdMaterial(layer0, teamColor, cliffTexOverride);
        StandardMaterial3D first = null;
        StandardMaterial3D previous = null;
        foreach (Variant layerV in layers)
        {
            GodotObject layer = layerV.AsGodotObject();
            string layerPath = "";
            int texId = GI(layer, "texture_id");
            if (texId >= 0 && texId < GA(model, "textures").Count)
                layerPath = ResolveTextureSlot(texId, teamColor, cliffTexOverride);
            StandardMaterial3D layerMat = MakeLayerMaterial(layer, layerPath, GI(layer, "filter_mode"));
            first ??= layerMat;
            if (previous != null)
                previous.NextPass = layerMat;
            previous = layerMat;
        }
        return first;
    }

    private ShaderMaterial MakeHdMaterial(GodotObject layer, int teamColor, string cliffTexOverride)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(HdShaderPath(GI(layer, "filter_mode"))) };
        int[] ids = GI32A(layer, "texture_ids");
        string[] uniformNames = { "diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map" };
        for (int slot = 0; slot < Math.Min(6, ids.Length); slot++)
        {
            string path = ResolveHdTextureSlot(ids[slot], slot, teamColor, cliffTexOverride);
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

    private void BindMaterialTracks(GDictionary opts)
    {
        if (!_playback || model == null)
            return;
        GArray materials = GA(model, "materials");
        if (materials.Count == 0)
            return;
        int teamColor = OI(opts, "team_color");
        string cliffTexture = OStr(opts, "cliff_texture");
        GArray geosets = GA(model, "geosets");
        foreach (Variant miV in _geoset_nodes)
        {
            var mi = miV.AsGodotObject() as MeshInstance3D;
            if (mi == null || !IsInstanceValid(mi))
                continue;
            int geosetId = mi.HasMeta("geoset_id") ? mi.GetMeta("geoset_id").AsInt32() : -1;
            if (geosetId < 0 || geosetId >= geosets.Count)
                continue;
            int materialId = Mathf.Clamp(GI(geosets[geosetId].AsGodotObject(), "material_id"), 0, materials.Count - 1);
            GArray layers = materials[materialId].AsGodotArray();
            Material materialPass = mi.MaterialOverride;
            foreach (Variant layerV in layers)
            {
                if (materialPass == null)
                    break;
                GodotObject layer = layerV.AsGodotObject();
                bool hasAlpha = GI32A(layer, "alpha_times").Length > 0;
                if (hasAlpha || LayerHasTextureTrack(layer))
                {
                    var entry = new GDictionary
                    {
                        ["layer"] = layer,
                        ["material"] = materialPass,
                        ["team_color"] = teamColor,
                        ["cliff_texture"] = cliffTexture,
                        ["last_texture_ids"] = new int[0],
                    };
                    _material_tracks.Add(entry);
                }
                materialPass = materialPass.NextPass;
            }
        }
    }

    private bool LayerHasTextureTrack(GodotObject layer)
    {
        foreach (Variant timesV in GA(layer, "texture_track_times"))
        {
            if (timesV.AsInt32Array().Length > 0)
                return true;
        }
        return false;
    }

    private GDictionary MaterialTrackClock(int globalSequenceId)
    {
        GArray globalSequences = GA(model, "global_sequences");
        if (globalSequenceId >= 0 && globalSequenceId < globalSequences.Count)
        {
            int duration = Math.Max(1, globalSequences[globalSequenceId].AsInt32());
            return new GDictionary { ["time"] = ((int)_global_time) % duration, ["start"] = -1, ["end"] = -1 };
        }
        int start = -1, end = -1;
        GArray sequences = GA(model, "sequences");
        if (_seq_index >= 0 && _seq_index < sequences.Count)
        {
            GodotObject seq = sequences[_seq_index].AsGodotObject();
            start = GI(seq, "interval_start");
            end = GI(seq, "interval_end");
        }
        return new GDictionary { ["time"] = CurrentTimeMs(), ["start"] = start, ["end"] = end };
    }

    public void _update_material_tracks()
    {
        foreach (Variant entryV in _material_tracks)
        {
            GDictionary entry = entryV.AsGodotDictionary();
            GodotObject layer = entry["layer"].AsGodotObject();
            var material = entry["material"].AsGodotObject() as Material;
            if (material == null || !IsInstanceValid(material))
                continue;
            GDictionary alphaClock = MaterialTrackClock(GI(layer, "alpha_global_sequence"));
            float alpha = W3MdxAnim.SampleFloat(
                GI32A(layer, "alpha_times"), GF32A(layer, "alpha_values"),
                alphaClock["time"].AsInt32(), GF(layer, "alpha"),
                alphaClock["start"].AsInt32(), alphaClock["end"].AsInt32());
            if (material is ShaderMaterial shaderMat)
                shaderMat.SetShaderParameter("layer_alpha", Mathf.Clamp(alpha, 0f, 1f));
            else if (material is StandardMaterial3D stdMat)
            {
                Color color = stdMat.AlbedoColor;
                color.A = Mathf.Clamp(alpha, 0f, 1f);
                stdMat.AlbedoColor = color;
            }

            int[] ids = GI32A(layer, "texture_ids");
            int[] slots = GI32A(layer, "texture_slots");
            int[] lastIds = entry["last_texture_ids"].AsInt32Array();
            if (lastIds.Length != ids.Length)
            {
                lastIds = new int[ids.Length];
                Array.Fill(lastIds, int.MinValue);
            }
            GArray textureTrackTimes = GA(layer, "texture_track_times");
            GArray textureTrackValues = GA(layer, "texture_track_values");
            GArray textureTrackGlobalSeqs = GA(layer, "texture_track_global_sequences");
            int textureCount = Math.Min(ids.Length, textureTrackTimes.Count);
            for (int textureIndex = 0; textureIndex < textureCount; textureIndex++)
            {
                int[] times = textureTrackTimes[textureIndex].AsInt32Array();
                if (times.Length == 0 || textureIndex >= textureTrackValues.Count)
                    continue;
                int[] values = textureTrackValues[textureIndex].AsInt32Array();
                int globalSequenceId = textureIndex < textureTrackGlobalSeqs.Count ? textureTrackGlobalSeqs[textureIndex].AsInt32() : -1;
                GDictionary clock = MaterialTrackClock(globalSequenceId);
                int textureId = W3MdxAnim.SampleIntStep(times, values, clock["time"].AsInt32(), ids[textureIndex],
                    clock["start"].AsInt32(), clock["end"].AsInt32());
                if (lastIds[textureIndex] == textureId)
                    continue;
                lastIds[textureIndex] = textureId;
                int pbrSlot = textureIndex < slots.Length ? slots[textureIndex] : textureIndex;
                if (material is ShaderMaterial sm)
                {
                    if (pbrSlot < 0 || pbrSlot >= 6)
                        continue;
                    string path = ResolveHdTextureSlot(textureId, pbrSlot, entry["team_color"].AsInt32(), entry["cliff_texture"].AsString());
                    if (!string.IsNullOrEmpty(path) && textures != null)
                    {
                        string[] uniformNames = { "diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map" };
                        sm.SetShaderParameter(uniformNames[pbrSlot], textures.Call("get_texture", path).As<Texture2D>());
                    }
                }
                else if (material is StandardMaterial3D stdMat2)
                {
                    string sdPath = ResolveTextureSlot(textureId, entry["team_color"].AsInt32(), entry["cliff_texture"].AsString());
                    if (!string.IsNullOrEmpty(sdPath) && textures != null)
                        stdMat2.AlbedoTexture = textures.Call("get_texture", sdPath).As<Texture2D>();
                }
            }
            entry["last_texture_ids"] = lastIds;
        }
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

    private StandardMaterial3D MakeLayerMaterial(GodotObject layer, string texPath, int layerFm)
    {
        Texture2D? texture = !string.IsNullOrEmpty(texPath) && textures != null
            ? textures.Call("get_texture", texPath).As<Texture2D>() : null;
        return W3ModelMaterials.CreateClassic(texture, texPath, layerFm,
            layer != null ? GI(layer, "flags") : 0,
            layer != null ? GF(layer, "alpha") : 1f);
    }

    private GArray BuildMeshes(GDictionary opts)
    {
        var bank = new GArray();
        int teamColor = OI(opts, "team_color");
        string cliffTexOverride = OStr(opts, "cliff_texture");
        GArray geosets = GA(model, "geosets");
        for (int gi = 0; gi < geosets.Count; gi++)
        {
            GodotObject g = geosets[gi].AsGodotObject();
            int lod = GI(g, "lod");
            if (lod != 0 && lod != -1)
                continue;
            ArrayMesh mesh = CommitGeosetMesh(g, GV3A(g, "vertices"), GV3A(g, "normals"));
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = MakeMaterial(g, teamColor, cliffTexOverride) };
            mi.SetMeta("geoset_id", gi);
            bool casts = GeosetCastsShadow(g);
            if (!casts)
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _mesh_root.AddChild(mi);
            _geoset_nodes.Add(mi);
            bank.Add(new GDictionary
            {
                ["mesh"] = mesh,
                ["mat"] = mi.MaterialOverride,
                ["geoset_id"] = gi,
                ["casts_shadow"] = casts,
            });
        }
        return bank;
    }

    private void BuildAnimatedMeshes(GDictionary opts)
    {
        int teamColor = OI(opts, "team_color");
        string cliffTexOverride = OStr(opts, "cliff_texture");
        BuildGpuSkeleton();
        GArray geosets = GA(model, "geosets");
        for (int gi = 0; gi < geosets.Count; gi++)
        {
            GodotObject g = geosets[gi].AsGodotObject();
            int lod = GI(g, "lod");
            if (lod != 0 && lod != -1)
                continue;
            _bind_geosets.Add(g);
            ArrayMesh mesh = CommitGpuSkinnedMesh(g);
            var mi = new MeshInstance3D { Mesh = mesh, Skin = _skin, MaterialOverride = MakeMaterial(g, teamColor, cliffTexOverride) };
            mi.SetMeta("geoset_id", gi);
            if (!GeosetCastsShadow(g))
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _mesh_root.AddChild(mi);
            mi.Skeleton = mi.GetPathTo(_skeleton);
            _geoset_nodes.Add(mi);
            _skinned_arrays.Add(mesh);
        }
    }

    private void BuildGpuSkeleton()
    {
        _skeleton = new Skeleton3D { Name = "MdxSkeleton" };
        AddChild(_skeleton);
        GArray bones = GA(model, "bones");
        foreach (Variant boneV in bones)
        {
            GodotObject bone = boneV.AsGodotObject();
            int index = _skeleton.GetBoneCount();
            string safeName = GS(bone, "name").Trim();
            foreach (char invalid in new[] { ':', '/', '\\', '%' })
                safeName = safeName.Replace(invalid.ToString(), "_");
            if (string.IsNullOrEmpty(safeName))
                safeName = "Bone";
            safeName = $"{safeName}_{GI(bone, "object_id")}";
            _skeleton.AddBone(safeName);
            _skeleton.SetBoneRest(index, Transform3D.Identity);
            _bone_index_by_oid[GI(bone, "object_id")] = index;
        }
        foreach (Variant boneV in bones)
        {
            GodotObject bone = boneV.AsGodotObject();
            int index = _bone_index_by_oid.ContainsKey(GI(bone, "object_id")) ? _bone_index_by_oid[GI(bone, "object_id")].AsInt32() : -1;
            int parentIndex = _bone_index_by_oid.ContainsKey(GI(bone, "parent_id")) ? _bone_index_by_oid[GI(bone, "parent_id")].AsInt32() : -1;
            if (index >= 0 && parentIndex >= 0 && parentIndex != index)
                _skeleton.SetBoneParent(index, parentIndex);
        }
        _skin = new Skin();
        for (int boneIndex = 0; boneIndex < _skeleton.GetBoneCount(); boneIndex++)
            _skin.AddBind(boneIndex, Transform3D.Identity);
    }

    private ArrayMesh CommitGpuSkinnedMesh(GodotObject g)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] vertices = GV3A(g, "vertices");
        Vector3[] normals = GV3A(g, "normals");
        Vector2[] uvs = GV2A(g, "uvs");
        int[] faces = GI32A(g, "faces");
        int[] boneIndices = GI32A(g, "bone_indices");
        float[] boneWeights = GF32A(g, "bone_weights");
        float[] tangents = GF32A(g, "tangents");
        int nvert = vertices.Length;
        for (int fi = 0; fi + 2 < faces.Length; fi += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int vertexIndex = faces[fi + k];
                if (vertexIndex < 0 || vertexIndex >= nvert)
                    continue;
                int[] bonesArr = { 0, 0, 0, 0 };
                float[] weightsArr = { 0f, 0f, 0f, 0f };
                float weightSum = 0f;
                for (int influence = 0; influence < 4; influence++)
                {
                    int sourceIndex = vertexIndex * 4 + influence;
                    if (sourceIndex >= boneIndices.Length || sourceIndex >= boneWeights.Length)
                        continue;
                    int objectId = boneIndices[sourceIndex];
                    float weight = boneWeights[sourceIndex];
                    if (weight <= 0.0001f || !_bone_index_by_oid.ContainsKey(objectId))
                        continue;
                    bonesArr[influence] = _bone_index_by_oid[objectId].AsInt32();
                    weightsArr[influence] = weight;
                    weightSum += weight;
                }
                if (weightSum <= 0.0001f)
                    weightsArr[0] = 1f;
                else if (Math.Abs(weightSum - 1f) > 0.001f)
                {
                    for (int influence = 0; influence < 4; influence++)
                        weightsArr[influence] /= weightSum;
                }
                st.SetBones(bonesArr);
                st.SetWeights(weightsArr);
                st.SetNormal(vertexIndex < normals.Length ? normals[vertexIndex] : Vector3.Up);
                if (tangents.Length >= (vertexIndex + 1) * 4)
                {
                    int ti = vertexIndex * 4;
                    st.SetTangent(new Plane(tangents[ti], tangents[ti + 1], tangents[ti + 2], tangents[ti + 3]));
                }
                if (vertexIndex < uvs.Length)
                    st.SetUV(uvs[vertexIndex]);
                st.AddVertex(vertices[vertexIndex]);
            }
        }
        return st.Commit();
    }

    private void BuildModelEffects(GDictionary opts)
    {
        if (model == null || !_animate)
            return;
        int teamColor = OI(opts, "team_color");
        foreach (Variant emitterV in GA(model, "particle_emitters2"))
        {
            GodotObject emitter = emitterV.AsGodotObject();
            GodotObject node = emitter.Get("node").AsGodotObject();
            var particles = new GpuParticles3D
            {
                Name = GS(node, "name"),
                LocalCoords = true,
            };
            float emissionRate = GF(emitter, "emission_rate");
            float lifeSpan = GF(emitter, "life_span");
            particles.Amount = Math.Clamp((int)Math.Ceiling(Math.Max(1.0, emissionRate) * Math.Max(0.05, lifeSpan)), 1, 512);
            particles.Lifetime = Mathf.Max(0.05f, lifeSpan);
            particles.VisibilityAabb = new Aabb(new Vector3(-256, -256, -256), new Vector3(512, 512, 512));
            var processMat = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(Mathf.Max(0.01f, GF(emitter, "width") * 0.5f), 0.01f, Mathf.Max(0.01f, GF(emitter, "length") * 0.5f)),
                Direction = Vector3.Up,
            };
            float latitude = GF(emitter, "latitude");
            processMat.Spread = Math.Abs(latitude) <= Mathf.Tau
                ? Mathf.Clamp(Mathf.RadToDeg(latitude), 0f, 180f)
                : Mathf.Clamp(latitude, 0f, 180f);
            processMat.Gravity = new Vector3(0f, -GF(emitter, "gravity"), 0f);
            float speed = GF(emitter, "speed");
            float speedVariation = GF(emitter, "speed_variation");
            processMat.InitialVelocityMin = Mathf.Max(0f, speed - Math.Abs(speedVariation));
            processMat.InitialVelocityMax = Mathf.Max(processMat.InitialVelocityMin, speed + Math.Abs(speedVariation));
            Color[] colors = GColA(emitter, "colors");
            if (colors.Length >= 3)
            {
                var gradient = new Gradient();
                gradient.SetColor(0, colors[0]);
                float timeMiddle = GF(emitter, "time_middle");
                gradient.AddPoint(Mathf.Clamp(timeMiddle, 0.001f, 0.999f), colors[1]);
                gradient.SetColor(gradient.GetPointCount() - 1, colors[2]);
                processMat.ColorRamp = new GradientTexture1D { Gradient = gradient };
            }
            float[] scaling = GF32A(emitter, "scaling");
            if (scaling.Length >= 3)
            {
                var scaleCurve = new Curve
                {
                    MinValue = 0f,
                    MaxValue = Mathf.Max(1f, Mathf.Max(scaling[0], Mathf.Max(scaling[1], scaling[2]))),
                };
                scaleCurve.AddPoint(new Vector2(0f, scaling[0]));
                scaleCurve.AddPoint(new Vector2(Mathf.Clamp(GF(emitter, "time_middle"), 0.001f, 0.999f), scaling[1]));
                scaleCurve.AddPoint(new Vector2(1f, scaling[2]));
                processMat.ScaleCurve = new CurveTexture { Curve = scaleCurve };
            }
            particles.ProcessMaterial = processMat;
            var quad = new QuadMesh { Size = Vector2.One };
            var drawMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
                ParticlesAnimHFrames = Math.Max(1, GI(emitter, "columns")),
                ParticlesAnimVFrames = Math.Max(1, GI(emitter, "rows")),
                ParticlesAnimLoop = false,
            };
            int filterMode = GI(emitter, "filter_mode");
            if (filterMode == 1 || filterMode == 3 || filterMode == 4)
                drawMat.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
            string particlePath = ParticleTexturePath(GI(emitter, "texture_id"), teamColor);
            if (!string.IsNullOrEmpty(particlePath) && textures != null)
                drawMat.AlbedoTexture = textures.Call("get_texture", particlePath).As<Texture2D>();
            quad.Material = drawMat;
            particles.DrawPass1 = quad;
            particles.Emitting = false;
            AddChild(particles);
            _particle_nodes.Add(new GDictionary { ["emitter"] = emitter, ["node"] = particles });
        }
        foreach (Variant lightV in GA(model, "lights"))
        {
            GodotObject light = lightV.AsGodotObject();
            if (GI(light, "type") != 0)
                continue;
            GodotObject node = light.Get("node").AsGodotObject();
            var omni = new OmniLight3D
            {
                Name = GS(node, "name"),
                ShadowEnabled = false,
                OmniRange = Mathf.Max(1f, GF(light, "attenuation_end")),
                LightColor = GCol(light, "color"),
                LightEnergy = 0f,
            };
            AddChild(omni);
            _light_nodes.Add(new GDictionary { ["light"] = light, ["node"] = omni });
        }
    }

    private string ParticleTexturePath(int texIndex, int teamColor)
    {
        GArray textureList = GA(model, "textures");
        if (texIndex < 0 || texIndex >= textureList.Count)
            return "";
        if (GI(model, "version") > 800 && !string.IsNullOrEmpty(textureList[texIndex].AsString()))
            return textureList[texIndex].AsString().GetBaseName() + ".dds";
        return ResolveTextureSlot(texIndex, teamColor);
    }

    private void SetEffectsActive(bool on)
    {
        foreach (Variant entryV in _particle_nodes)
        {
            var particles = entryV.AsGodotDictionary()["node"].AsGodotObject() as GpuParticles3D;
            if (particles != null && IsInstanceValid(particles))
                particles.Emitting = on && particles.Emitting;
        }
        foreach (Variant entryV in _light_nodes)
        {
            var light = entryV.AsGodotDictionary()["node"].AsGodotObject() as Light3D;
            if (light != null && IsInstanceValid(light) && !on)
                light.LightEnergy = 0f;
        }
    }

    private ArrayMesh CommitGeosetMesh(GodotObject g, Vector3[] verts, Vector3[] norms)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int[] faces = GI32A(g, "faces");
        float[] tangents = GF32A(g, "tangents");
        Vector2[] uvs = GV2A(g, "uvs");
        int nvert = verts.Length;
        for (int fi = 0; fi + 2 < faces.Length; fi += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int idx = faces[fi + k];
                if (idx < 0 || idx >= nvert)
                    continue;
                Vector3 nrm = idx < norms.Length ? norms[idx] : Vector3.Up;
                Vector3 vtx = verts[idx];
                if (_cliff_remap)
                {
                    vtx = new Vector3(vtx.Z, vtx.Y, -vtx.X);
                    nrm = new Vector3(nrm.Z, nrm.Y, -nrm.X).Normalized();
                }
                if (StaticVertexTransform != null)
                    (vtx, nrm) = StaticVertexTransform(vtx, nrm);
                st.SetNormal(nrm);
                if (tangents.Length >= (idx + 1) * 4)
                {
                    var tangent = new Vector3(tangents[idx * 4], tangents[idx * 4 + 1], tangents[idx * 4 + 2]);
                    if (_cliff_remap)
                        tangent = new Vector3(tangent.Z, tangent.Y, -tangent.X).Normalized();
                    st.SetTangent(new Plane(tangent, tangents[idx * 4 + 3]));
                }
                if (idx < uvs.Length)
                    st.SetUV(uvs[idx]);
                st.AddVertex(vtx);
            }
        }
        return st.Commit();
    }

    private string TextureForGeoset(GodotObject g, int teamColor = 0, string cliffTexOverride = "")
    {
        if (_cliff_remap && !string.IsNullOrEmpty(cliffTexOverride))
            return cliffTexOverride;
        GArray materials = GA(model, "materials");
        if (materials.Count == 0)
            return ResolveTextureSlot(0, teamColor, cliffTexOverride);
        int mid = Mathf.Clamp(GI(g, "material_id"), 0, materials.Count - 1);
        GArray layers = materials[mid].AsGodotArray();
        if (layers.Count == 0)
            return "";
        GodotObject layer = layers[0].AsGodotObject();
        int texId = GI(layer, "texture_id");
        if (texId >= 0 && texId < GA(model, "textures").Count)
            return ResolveTextureSlot(texId, teamColor, cliffTexOverride);
        return "";
    }

    private string ResolveTextureSlot(int texIndex, int teamColor = 0, string cliffTexOverride = "")
    {
        GArray textureList = GA(model, "textures");
        if (texIndex < 0 || texIndex >= textureList.Count)
            return "";
        string path = textureList[texIndex].AsString();
        if (!string.IsNullOrEmpty(path))
            return path;
        int[] replaceableIds = GI32A(model, "texture_replaceable_ids");
        int rid = texIndex < replaceableIds.Length ? replaceableIds[texIndex] : 0;
        if (rid == 11 && !string.IsNullOrEmpty(cliffTexOverride))
            return cliffTexOverride;
        if (rid == 1 || rid == 2)
        {
            string stem = rid == 1 ? "ReplaceableTextures/TeamColor/TeamColor" : "ReplaceableTextures/TeamGlow/TeamGlow";
            return $"{stem}{Mathf.Clamp(teamColor, 0, 15):D2}.blp";
        }
        if (!string.IsNullOrEmpty(_replaceable_tex) && rid >= 31)
        {
            string rt = _replaceable_tex.Replace("\\", "/");
            if (!rt.ToLowerInvariant().EndsWith(".blp"))
                rt += ".blp";
            return rt;
        }
        return ReplaceableRelPath(rid);
    }

    private string ResolveHdTextureSlot(int texIndex, int pbrSlot, int teamColor = 0, string cliffTexOverride = "")
    {
        GArray textureList = GA(model, "textures");
        if (texIndex < 0 || texIndex >= textureList.Count)
            return "";
        string authored = textureList[texIndex].AsString();
        if (!string.IsNullOrEmpty(authored))
            return authored.GetBaseName() + ".dds";
        int[] replaceableIds = GI32A(model, "texture_replaceable_ids");
        int rid = texIndex < replaceableIds.Length ? replaceableIds[texIndex] : 0;
        if (rid == 11 && !string.IsNullOrEmpty(cliffTexOverride))
            return cliffTexOverride.GetBaseName() + HdSlotSuffix(pbrSlot) + ".dds";
        if (rid == 1 || rid == 2)
        {
            string teamStem = rid == 1 ? "ReplaceableTextures/TeamColor/TeamColor" : "ReplaceableTextures/TeamGlow/TeamGlow";
            return $"{teamStem}{Mathf.Clamp(teamColor, 0, 23):D2}.dds";
        }
        string stem = "";
        if (!string.IsNullOrEmpty(_replaceable_tex) && rid >= 31)
            stem = _replaceable_tex.Replace("\\", "/").GetBaseName();
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

    // mdx-m3-viewer replaceableids.js ? TEXS ???? + replaceableId ???????
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

    private static string ReplaceableRelPath(int replaceableId) =>
        ReplaceableStems.TryGetValue(replaceableId, out string stem) ? stem + ".blp" : "";

    // ---- ???? --------------------------------------------------------

    public void play_named(string seqName, bool restart = false)
    {
        if (model == null)
            return;
        int idx = model.Call("find_sequence", seqName).AsInt32();
        if (idx < 0)
            idx = model.Call("find_idle_sequence").AsInt32();
        _loop = true;
        ApplySequenceIndex(idx, restart);
    }

    public bool play_named_once(string seqName)
    {
        if (model == null)
            return false;
        int idx = model.Call("find_sequence", seqName).AsInt32();
        if (idx < 0)
            return false;
        _loop = false;
        ApplySequenceIndex(idx, true);
        return true;
    }

    public void play_idle()
    {
        if (model == null)
            return;
        _loop = true;
        ApplySequenceIndex(model.Call("find_idle_sequence").AsInt32());
    }

    private void ApplySequenceIndex(int idx, bool restart = false)
    {
        if (idx < 0)
        {
            _seq_index = -1;
            return;
        }
        if (idx == _seq_index && !restart)
        {
            ApplyGeosetVisibility();
            _update_material_tracks();
            if (_animate && _anim_active)
                SkinAtCurrentTime();
            return;
        }
        _seq_index = idx;
        _anim_time = 0f;
        ApplyGeosetVisibility();
        _update_material_tracks();
        if (_animate && _anim_active)
            SkinAtCurrentTime();
    }

    public void set_moving(bool moving)
    {
        if (moving)
            play_named("Walk");
        else
            play_idle();
    }

    private void ApplyGeosetVisibility()
    {
        if (model == null || _mesh_root == null)
            return;
        int timeMs = 0;
        GArray sequences = GA(model, "sequences");
        if (_seq_index >= 0 && _seq_index < sequences.Count)
        {
            GodotObject s = sequences[_seq_index].AsGodotObject();
            timeMs = GI(s, "interval_start") + (int)_anim_time;
        }
        foreach (Variant miV in _geoset_nodes)
        {
            var mi = miV.AsGodotObject() as MeshInstance3D;
            if (mi == null || !IsInstanceValid(mi))
                continue;
            int gid = mi.HasMeta("geoset_id") ? mi.GetMeta("geoset_id").AsInt32() : -1;
            if (gid < 0)
            {
                mi.Visible = true;
                continue;
            }
            mi.Visible = model.Call("geoset_alpha_at", gid, timeMs).AsSingle() > 0.01f;
        }
    }

    private int CurrentTimeMs()
    {
        GArray sequences = GA(model, "sequences");
        if (_seq_index < 0 || _seq_index >= sequences.Count)
            return 0;
        GodotObject s = sequences[_seq_index].AsGodotObject();
        return GI(s, "interval_start") + (int)_anim_time;
    }

    private void SkinAtCurrentTime()
    {
        if (!_animate || model == null || GA(model, "bones").Count == 0)
            return;
        int timeMs = CurrentTimeMs();
        int seqStart = -1, seqEnd = -1;
        GArray sequences = GA(model, "sequences");
        if (_seq_index >= 0 && _seq_index < sequences.Count)
        {
            GodotObject s = sequences[_seq_index].AsGodotObject();
            seqStart = GI(s, "interval_start");
            seqEnd = GI(s, "interval_end");
        }
        GDictionary worlds = W3MdxAnim.ComputeWorldMatrices(GA(model, "bones"), timeMs, seqStart, seqEnd);
        if (_skeleton != null && IsInstanceValid(_skeleton))
        {
            UpdateGpuSkeleton(worlds);
            _update_model_effects(worlds, timeMs, seqStart, seqEnd);
            return;
        }
        for (int gi = 0; gi < _geoset_nodes.Count; gi++)
        {
            var mi = _geoset_nodes[gi].AsGodotObject() as MeshInstance3D;
            if (mi == null || !IsInstanceValid(mi))
                continue;
            if (gi >= _bind_geosets.Count)
                continue;
            GodotObject g = _bind_geosets[gi].AsGodotObject();
            Vector3[] verticesWar3 = GV3A(g, "vertices_war3");
            int nvert = verticesWar3.Length;
            if (nvert == 0)
                continue;
            Vector3[] normalsWar3 = GV3A(g, "normals_war3");
            int[] boneIndices = GI32A(g, "bone_indices");
            float[] boneWeights = GF32A(g, "bone_weights");
            var skinnedV = new Vector3[nvert];
            var skinnedN = new Vector3[nvert];
            for (int vi = 0; vi < nvert; vi++)
            {
                Vector3 vw = verticesWar3[vi];
                Vector3 nw = vi < normalsWar3.Length ? normalsWar3[vi] : Vector3.Up;
                Vector3 sw = W3MdxAnim.SkinVertex(vw, boneIndices, boneWeights, vi, worlds);
                Vector3 sn = W3MdxAnim.SkinNormal(nw, boneIndices, boneWeights, vi, worlds);
                skinnedV[vi] = W3ToGodot(sw);
                skinnedN[vi] = W3ToGodot(sn).Normalized();
                mi.Mesh = CommitGeosetMesh(g, skinnedV, skinnedN);
            }
        }
        _update_model_effects(worlds, timeMs, seqStart, seqEnd);
    }

    private void UpdateGpuSkeleton(GDictionary worlds)
    {
        foreach (Variant boneV in GA(model, "bones"))
        {
            GodotObject bone = boneV.AsGodotObject();
            int objectId = GI(bone, "object_id");
            int boneIndex = _bone_index_by_oid.ContainsKey(objectId) ? _bone_index_by_oid[objectId].AsInt32() : -1;
            if (boneIndex < 0 || !worlds.ContainsKey(objectId))
                continue;
            Transform3D localWar3 = worlds[objectId].AsTransform3D();
            int parentId = GI(bone, "parent_id");
            if (parentId >= 0 && worlds.ContainsKey(parentId))
                localWar3 = worlds[parentId].AsTransform3D().AffineInverse() * localWar3;
            Transform3D localGodot = War3TransformToGodot(localWar3);
            Vector3 poseScale = localGodot.Basis.Scale;
            Quaternion poseRotation = localGodot.Basis.Orthonormalized().GetRotationQuaternion();
            _skeleton.SetBonePosePosition(boneIndex, localGodot.Origin);
            _skeleton.SetBonePoseRotation(boneIndex, poseRotation);
            _skeleton.SetBonePoseScale(boneIndex, poseScale);
        }
    }

    public void _update_model_effects(GDictionary worlds, int timeMs, int seqStart, int seqEnd)
    {
        foreach (Variant entryV in _particle_nodes)
        {
            GDictionary entry = entryV.AsGodotDictionary();
            GodotObject emitter = entry["emitter"].AsGodotObject();
            var particles = entry["node"].AsGodotObject() as GpuParticles3D;
            if (particles == null || !IsInstanceValid(particles))
                continue;
            GodotObject node = emitter.Get("node").AsGodotObject();
            int objectId = GI(node, "object_id");
            if (worlds.ContainsKey(objectId))
                particles.Transform = War3TransformToGodot(worlds[objectId].AsTransform3D());
            float visibility = W3MdxAnim.SampleFloat(
                GI32A(emitter, "visibility_times"), GF32A(emitter, "visibility_values"), timeMs, 1f, seqStart, seqEnd);
            float rate = W3MdxAnim.SampleFloat(
                GI32A(emitter, "emission_rate_times"), GF32A(emitter, "emission_rate_values"), timeMs,
                GF(emitter, "emission_rate"), seqStart, seqEnd);
            particles.Emitting = _anim_active && visibility > 0.01f && rate > 0.01f;
        }
        foreach (Variant entryV in _light_nodes)
        {
            GDictionary entry = entryV.AsGodotDictionary();
            GodotObject info = entry["light"].AsGodotObject();
            var light = entry["node"].AsGodotObject() as OmniLight3D;
            if (light == null || !IsInstanceValid(light))
                continue;
            GodotObject node = info.Get("node").AsGodotObject();
            int objectId = GI(node, "object_id");
            if (worlds.ContainsKey(objectId))
                light.Transform = War3TransformToGodot(worlds[objectId].AsTransform3D());
            float visibility = W3MdxAnim.SampleFloat(
                GI32A(info, "visibility_times"), GF32A(info, "visibility_values"), timeMs, 1f, seqStart, seqEnd);
            float intensity = W3MdxAnim.SampleFloat(
                GI32A(info, "intensity_times"), GF32A(info, "intensity_values"), timeMs, GF(info, "intensity"), seqStart, seqEnd);
            float attenuationEnd = W3MdxAnim.SampleFloat(
                GI32A(info, "attenuation_end_times"), GF32A(info, "attenuation_end_values"), timeMs,
                GF(info, "attenuation_end"), seqStart, seqEnd);
            Color infoColor = GCol(info, "color");
            Vector3 colorVec = W3MdxAnim.SampleVec3(
                GI32A(info, "color_times"), GV3A(info, "color_values"), timeMs,
                new Vector3(infoColor.R, infoColor.G, infoColor.B), seqStart, seqEnd);
            light.LightColor = new Color(colorVec.X, colorVec.Y, colorVec.Z);
            light.OmniRange = Mathf.Max(1f, attenuationEnd);
            light.LightEnergy = _anim_active ? Mathf.Max(0f, intensity * visibility) : 0f;
        }
    }

    // War3?X,Y,Z-up?-> Godot?X,Y-up,Z??P * M * P^-1 ????
    private static Transform3D War3TransformToGodot(Transform3D xf)
    {
        Vector3 gx = W3ToGodot(xf.Basis * new Vector3(1, 0, 0));
        Vector3 gy = W3ToGodot(xf.Basis * new Vector3(0, 0, 1));
        Vector3 gz = W3ToGodot(xf.Basis * new Vector3(0, 1, 0));
        return new Transform3D(new Basis(gx, gy, gz), W3ToGodot(xf.Origin));
    }

    public static Vector3 W3ToGodot(Vector3 v) => new(v.X, v.Z, v.Y);

    public override void _Process(double delta)
    {
        if (!_playing || !_anim_active || model == null || !_playback)
            return;
        _global_time += (float)delta * 1000f;
        GArray sequences = GA(model, "sequences");
        if (_seq_index < 0 || _seq_index >= sequences.Count)
        {
            _update_material_tracks();
            return;
        }
        GodotObject s = sequences[_seq_index].AsGodotObject();
        float span = Math.Max(1, GI(s, "interval_end") - GI(s, "interval_start"));
        if (_loop)
            _anim_time = Mathf.PosMod(_anim_time + (float)delta * 1000f, span);
        else
            _anim_time = Mathf.Min(_anim_time + (float)delta * 1000f, span - 1f);
        ApplyGeosetVisibility();
        _update_material_tracks();
        if (!_animate)
            return;
        _skin_frame++;
        if (_skin_frame % SkinEveryNFrames == 0)
            SkinAtCurrentTime();
    }
}
