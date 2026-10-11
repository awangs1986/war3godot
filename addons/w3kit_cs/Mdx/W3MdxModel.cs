using System;
using System.Collections.Generic;
using Godot;
using W3Compat.Mdx;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// MDX800 模型门面的 C# 版本：取代 addons/w3kit/w3_mdx_model.gd。真正的二进制
/// 解析复用 W3Compat.Core.Mdx.MdxModel（与 standalone 编辑器同源）；本类负责把
/// 解析结果重建成旧 GDS 门面同形的对象图（Sequence/Geoset/Bone/LayerInfo/...
/// 均为独立 GodotObject），字段/方法名保持 snake_case 与旧门面完全一致，供
/// W3MdxInstance.cs/W3MdxAnim.cs 的 Get()/Call() 鸭子类型访问、以及 tools/*.gd
/// 的 "." 属性访问零改动地继续工作（这些字段是普通 public 字段而非 [Export]——
/// 与 W3SimUnit.cs 同款约定：Godot 的 C# 脚本实例对 Get()/Call() 的支持覆盖所有
/// public 成员，不要求 [Export]）。
/// </summary>
[GlobalClass]
public partial class W3MdxModel : RefCounted
{
    public const int MAX_ELEMENTS = 2_000_000;
    public const int MAX_TRACKS = 1_000_000;
    public const int MAX_LAYERS = 4096;

    /// <summary>mdx-m3-viewer replaceableids.js —— 空 TEXS 路径 + replaceableId 时使用。</summary>
    public static readonly GDictionary REPLACEABLE_STEMS = new()
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

    private MdxModel? _model;

    public int version = 800;
    public string name = "";
    public GArray sequences = new();
    public int[] global_sequences = Array.Empty<int>();
    public GArray geosets = new();
    public GArray bones = new();
    public string[] textures = Array.Empty<string>();
    public int[] texture_replaceable_ids = Array.Empty<int>();
    public GArray materials = new();
    public Vector3[] pivots = Array.Empty<Vector3>();
    public GDictionary geoset_anims = new();
    public GArray attachments = new();
    public GArray particle_emitters2 = new();
    public GArray event_objects = new();
    public GArray lights = new();

    // ---- 加载 --------------------------------------------------------

    public static W3MdxModel? LoadFile(string path)
    {
        MdxModel? model = MdxModel.LoadFile(path);
        return model == null ? null : FromModel(model);
    }

    public static W3MdxModel? load_file(string path) => LoadFile(path);

    public static W3MdxModel? LoadBytes(byte[] data)
    {
        MdxModel? model = MdxModel.LoadBytes(data);
        return model == null ? null : FromModel(model);
    }

    public static W3MdxModel? load_bytes(byte[] data) => LoadBytes(data);

    /// <summary>旧 GDS 的越界元素数校验；tools/w3_regression.gd 直接以此名调用维持兼容。</summary>
    public static int _bounded_count(int raw, int bytesRemaining, int bytesPerItem, int maximum)
    {
        if (raw < 0 || raw > maximum || bytesPerItem <= 0)
            return 0;
        if (raw > bytesRemaining / bytesPerItem)
            return 0;
        return raw;
    }

    // ---- 查询方法（委托给 W3Compat.Core.Mdx.MdxModel）--------------------

    public bool HasLayerAnimation() => _model?.HasLayerAnimation() ?? false;
    public bool has_layer_animation() => HasLayerAnimation();

    public bool HasLayerAnimationForSequence(int sequenceIndex) => _model?.HasLayerAnimationForSequence(sequenceIndex) ?? false;
    public bool has_layer_animation_for_sequence(int sequenceIndex) => HasLayerAnimationForSequence(sequenceIndex);

    public bool HasIdleLayerAnimation() => _model?.HasIdleLayerAnimation() ?? false;
    public bool has_idle_layer_animation() => HasIdleLayerAnimation();

    public bool HasSequenceBoneMotion(int sequenceIndex) => _model?.HasSequenceBoneMotion(sequenceIndex) ?? false;
    public bool has_sequence_bone_motion(int sequenceIndex) => HasSequenceBoneMotion(sequenceIndex);

    public bool HasIdleBoneMotion() => _model?.HasIdleBoneMotion() ?? false;
    public bool has_idle_bone_motion() => HasIdleBoneMotion();

    public int FindSequence(string query) => _model?.FindSequence(query) ?? -1;
    public int find_sequence(string query) => FindSequence(query);

    public int FindIdleSequence() => _model?.FindIdleSequence() ?? -1;
    public int find_idle_sequence() => FindIdleSequence();

    public float GeosetAlphaAt(int geosetId, int timeMs) => _model?.GeosetAlphaAt(geosetId, timeMs) ?? 1f;
    public float geoset_alpha_at(int geosetId, int timeMs) => GeosetAlphaAt(geosetId, timeMs);

    public static Vector3 W3ToGodot(Vector3 v) => new(v.X, v.Z, v.Y);
    public static Vector3 w3_to_godot(Vector3 v) => W3ToGodot(v);

    public static string ReplaceableRelPath(int replaceableId) =>
        REPLACEABLE_STEMS.TryGetValue(replaceableId, out Variant stem) ? stem.AsString() + ".blp" : "";
    public static string replaceable_rel_path(int replaceableId) => ReplaceableRelPath(replaceableId);

    /// <summary>不带扩展名的可替换纹理词干；REPLACEABLE_STEMS 字典本身不通过静态字段
    /// 暴露给 GDScript（C# 静态字段无法保证被脚本常量查找识别），改走此方法。</summary>
    public static string ReplaceableStem(int replaceableId) =>
        REPLACEABLE_STEMS.TryGetValue(replaceableId, out Variant stem) ? stem.AsString() : "";
    public static string replaceable_stem(int replaceableId) => ReplaceableStem(replaceableId);

    // ---- 快照 -> 对象图 --------------------------------------------------

    private static W3MdxModel FromModel(MdxModel model)
    {
        var boneMap = new Dictionary<MdxNode, W3MdxBoneData>(ReferenceEqualityComparer.Instance);
        var bones = new GArray();
        foreach (MdxNode node in model.Bones)
        {
            W3MdxBoneData data = BoneData(node);
            boneMap[node] = data;
            bones.Add(data);
        }

        var sequences = new GArray();
        foreach (MdxSequence sequence in model.Sequences)
            sequences.Add(SequenceData(sequence));

        var geosets = new GArray();
        foreach (MdxGeoset geoset in model.Geosets)
            geosets.Add(GeosetData(geoset));

        var materials = new GArray();
        foreach (List<MdxLayer> layers in model.Materials)
        {
            var layerArray = new GArray();
            foreach (MdxLayer layer in layers)
                layerArray.Add(LayerData(layer));
            materials.Add(layerArray);
        }

        var geosetAnims = new GDictionary();
        foreach (KeyValuePair<int, MdxGeosetAnimation> pair in model.GeosetAnimations)
            geosetAnims[pair.Key] = GeosetAnimData(pair.Value);

        var attachments = new GArray();
        foreach (MdxAttachment attachment in model.Attachments)
            attachments.Add(AttachmentData(attachment, boneMap));

        var particleEmitters2 = new GArray();
        foreach (MdxParticleEmitter2 emitter in model.ParticleEmitters2)
            particleEmitters2.Add(ParticleEmitter2Data(emitter, boneMap));

        var eventObjects = new GArray();
        foreach (MdxEventObject item in model.EventObjects)
            eventObjects.Add(EventObjectData(item, boneMap));

        var lights = new GArray();
        foreach (MdxLight light in model.Lights)
            lights.Add(LightData(light, boneMap));

        return new W3MdxModel
        {
            _model = model,
            version = model.Version,
            name = model.Name,
            sequences = sequences,
            global_sequences = ToIntArray(model.GlobalSequences),
            geosets = geosets,
            bones = bones,
            textures = TexturesArray(model.Textures),
            texture_replaceable_ids = TextureReplaceableIdsArray(model.Textures),
            materials = materials,
            pivots = ToVector3ArrayRaw(model.Pivots),
            geoset_anims = geosetAnims,
            attachments = attachments,
            particle_emitters2 = particleEmitters2,
            event_objects = eventObjects,
            lights = lights,
        };
    }

    private static W3MdxSequenceData SequenceData(MdxSequence sequence) => new()
    {
        name = sequence.Name,
        interval_start = sequence.IntervalStart,
        interval_end = sequence.IntervalEnd,
        move_speed = sequence.MoveSpeed,
        flags = sequence.Flags,
        rarity = sequence.Rarity,
        sync_point = sequence.SyncPoint,
        // 旧 GDS 实现不对 extent_min/max 做 War3->Godot 轴变换（字段未被下游使用，
        // 保留原始 War3 分量以维持字节级兼容）。
        extent_min = ToVector3Raw(sequence.ExtentMin),
        extent_max = ToVector3Raw(sequence.ExtentMax),
        extent_radius = sequence.ExtentRadius,
    };

    private static W3MdxGeosetData GeosetData(MdxGeoset geoset)
    {
        // vertices_war3/normals_war3 保持原始 War3 Z-up（蒙皮计算用）；
        // vertices/normals 做 (x,y,z)->(x,z,y) 换算供 Godot 渲染。
        int vertCount = geoset.VerticesWar3.Count;
        int normCount = geoset.NormalsWar3.Count;
        var verticesWar3 = ToVector3ArrayRaw(geoset.VerticesWar3);
        var normalsWar3 = ToVector3ArrayRaw(geoset.NormalsWar3);
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[normCount];
        for (int i = 0; i < vertCount; i++)
            vertices[i] = W3ToGodot(verticesWar3[i]);
        for (int i = 0; i < normCount; i++)
            normals[i] = W3ToGodot(normalsWar3[i]).Normalized();

        var tangents = new float[geoset.Tangents.Count * 4];
        for (int i = 0; i < geoset.Tangents.Count; i++)
        {
            System.Numerics.Vector4 t = geoset.Tangents[i];
            Vector3 tg = W3ToGodot(new Vector3(t.X, t.Y, t.Z)).Normalized();
            tangents[i * 4] = tg.X;
            tangents[i * 4 + 1] = tg.Y;
            tangents[i * 4 + 2] = tg.Z;
            tangents[i * 4 + 3] = t.W;
        }

        return new W3MdxGeosetData
        {
            vertices = vertices,
            normals = normals,
            vertices_war3 = verticesWar3,
            normals_war3 = normalsWar3,
            uvs = ToVector2Array(geoset.Uvs),
            faces = ToIntArray(geoset.Faces),
            material_id = geoset.MaterialId,
            lod = geoset.Lod,
            lod_name = geoset.LodName,
            vertex_groups = ToByteArray(geoset.VertexGroups),
            matrix_group_sizes = ToIntArray(geoset.MatrixGroupSizes),
            matrix_indices = ToIntArray(geoset.MatrixIndices),
            bone_indices = ToIntArray(geoset.BoneIndices),
            bone_weights = ToFloatArray(geoset.BoneWeights),
            tangents = tangents,
        };
    }

    private static W3MdxBoneData BoneData(MdxNode node)
    {
        var rotationValues = new GArray();
        foreach (System.Numerics.Quaternion q in node.Rotation.Values)
            rotationValues.Add(new Quaternion(q.X, q.Y, q.Z, q.W));
        return new W3MdxBoneData
        {
            name = node.Name,
            object_id = node.ObjectId,
            parent_id = node.ParentId,
            pivot = ToVector3Raw(node.Pivot),
            translation_times = ToIntArray(node.Translation.Times),
            translation_values = ToVector3ArrayRaw(node.Translation.Values),
            rotation_times = ToIntArray(node.Rotation.Times),
            rotation_values = rotationValues,
            scale_times = ToIntArray(node.Scale.Times),
            scale_values = ToVector3ArrayRaw(node.Scale.Values),
        };
    }

    private static W3MdxLayerData LayerData(MdxLayer layer)
    {
        var textureTrackTimes = new GArray();
        var textureTrackValues = new GArray();
        var textureTrackGlobalSequences = new int[layer.TextureTracks.Count];
        for (int i = 0; i < layer.TextureTracks.Count; i++)
        {
            MdxTrack<int> track = layer.TextureTracks[i];
            textureTrackTimes.Add(ToIntArray(track.Times));
            textureTrackValues.Add(ToIntArray(track.Values));
            textureTrackGlobalSequences[i] = track.GlobalSequenceId;
        }
        return new W3MdxLayerData
        {
            texture_id = layer.TextureId,
            texture_ids = ToIntArray(layer.TextureIds),
            texture_slots = ToIntArray(layer.TextureSlots),
            texture_track_times = textureTrackTimes,
            texture_track_values = textureTrackValues,
            texture_track_global_sequences = textureTrackGlobalSequences,
            filter_mode = layer.FilterMode,
            flags = layer.Flags,
            alpha = layer.Alpha,
            alpha_times = ToIntArray(layer.AlphaTrack.Times),
            alpha_values = ToFloatArray(layer.AlphaTrack.Values),
            alpha_global_sequence = layer.AlphaTrack.GlobalSequenceId,
            emissive_gain = layer.EmissiveGain,
            shader = layer.Shader,
        };
    }

    private static W3MdxGeosetAnimData GeosetAnimData(MdxGeosetAnimation animation) => new()
    {
        geoset_id = animation.GeosetId,
        static_alpha = animation.StaticAlpha,
        alpha_times = ToIntArray(animation.Alpha.Times),
        alpha_values = ToFloatArray(animation.Alpha.Values),
    };

    private static W3MdxAttachmentData AttachmentData(MdxAttachment attachment, Dictionary<MdxNode, W3MdxBoneData> boneMap) => new()
    {
        node = boneMap.GetValueOrDefault(attachment.Node),
        path = attachment.Path,
        attachment_id = attachment.AttachmentId,
    };

    private static W3MdxParticleEmitter2Data ParticleEmitter2Data(MdxParticleEmitter2 emitter, Dictionary<MdxNode, W3MdxBoneData> boneMap)
    {
        var colors = new Color[emitter.Colors.Count];
        for (int i = 0; i < emitter.Colors.Count; i++)
        {
            System.Numerics.Vector3 c = emitter.Colors[i];
            float a = i < emitter.ColorAlphas.Count ? emitter.ColorAlphas[i] : 1f;
            colors[i] = new Color(c.X, c.Y, c.Z, a);
        }
        return new W3MdxParticleEmitter2Data
        {
            node = boneMap.GetValueOrDefault(emitter.Node),
            speed = emitter.Speed,
            speed_variation = emitter.SpeedVariation,
            latitude = emitter.Latitude,
            gravity = emitter.Gravity,
            life_span = emitter.LifeSpan,
            emission_rate = emitter.EmissionRate,
            length = emitter.Length,
            width = emitter.Width,
            filter_mode = emitter.FilterMode,
            rows = emitter.Rows,
            columns = emitter.Columns,
            head_or_tail = emitter.HeadOrTail,
            tail_length = emitter.TailLength,
            time_middle = emitter.TimeMiddle,
            colors = colors,
            scaling = ToFloatArray(emitter.Scaling),
            texture_id = emitter.TextureId,
            replaceable_id = emitter.ReplaceableId,
            visibility_times = ToIntArray(emitter.Visibility.Times),
            visibility_values = ToFloatArray(emitter.Visibility.Values),
            emission_rate_times = ToIntArray(emitter.EmissionRateTrack.Times),
            emission_rate_values = ToFloatArray(emitter.EmissionRateTrack.Values),
        };
    }

    private static W3MdxEventObjectData EventObjectData(MdxEventObject item, Dictionary<MdxNode, W3MdxBoneData> boneMap) => new()
    {
        node = boneMap.GetValueOrDefault(item.Node),
        global_sequence_id = item.GlobalSequenceId,
        times = ToIntArray(item.Times),
    };

    private static W3MdxLightData LightData(MdxLight light, Dictionary<MdxNode, W3MdxBoneData> boneMap) => new()
    {
        node = boneMap.GetValueOrDefault(light.Node),
        type = light.Type,
        attenuation_start = light.AttenuationStart,
        attenuation_end = light.AttenuationEnd,
        color = new Color(light.Color.X, light.Color.Y, light.Color.Z, 1f),
        intensity = light.Intensity,
        ambient_color = new Color(light.AmbientColor.X, light.AmbientColor.Y, light.AmbientColor.Z, 1f),
        ambient_intensity = light.AmbientIntensity,
        intensity_times = ToIntArray(light.IntensityTrack.Times),
        intensity_values = ToFloatArray(light.IntensityTrack.Values),
        visibility_times = ToIntArray(light.VisibilityTrack.Times),
        visibility_values = ToFloatArray(light.VisibilityTrack.Values),
        color_times = ToIntArray(light.ColorTrack.Times),
        color_values = ToVector3ArrayRaw(light.ColorTrack.Values),
        attenuation_end_times = ToIntArray(light.AttenuationEndTrack.Times),
        attenuation_end_values = ToFloatArray(light.AttenuationEndTrack.Values),
    };

    // ---- 基础类型转换（同 W3MdxModelBridge.cs）------------------------------

    private static string[] TexturesArray(List<MdxTexture> textures)
    {
        var array = new string[textures.Count];
        for (int i = 0; i < textures.Count; i++)
            array[i] = textures[i].Path;
        return array;
    }

    private static int[] TextureReplaceableIdsArray(List<MdxTexture> textures)
    {
        var array = new int[textures.Count];
        for (int i = 0; i < textures.Count; i++)
            array[i] = textures[i].ReplaceableId;
        return array;
    }

    private static int[] ToIntArray(IReadOnlyList<int> values)
    {
        var array = new int[values.Count];
        for (int i = 0; i < values.Count; i++)
            array[i] = values[i];
        return array;
    }

    private static float[] ToFloatArray(IReadOnlyList<float> values)
    {
        var array = new float[values.Count];
        for (int i = 0; i < values.Count; i++)
            array[i] = values[i];
        return array;
    }

    private static byte[] ToByteArray(IReadOnlyList<byte> values)
    {
        var array = new byte[values.Count];
        for (int i = 0; i < values.Count; i++)
            array[i] = values[i];
        return array;
    }

    private static Vector3[] ToVector3ArrayRaw(IReadOnlyList<System.Numerics.Vector3> values)
    {
        var array = new Vector3[values.Count];
        for (int i = 0; i < values.Count; i++)
            array[i] = ToVector3Raw(values[i]);
        return array;
    }

    private static Vector3 ToVector3Raw(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector2[] ToVector2Array(IReadOnlyList<System.Numerics.Vector2> values)
    {
        var array = new Vector2[values.Count];
        for (int i = 0; i < values.Count; i++)
            array[i] = new Vector2(values[i].X, values[i].Y);
        return array;
    }
}

/// <summary>MDX 动画片段（旧 GDS Sequence 类的 C# 等价物）。</summary>
public partial class W3MdxSequenceData : RefCounted
{
    public string name = "";
    public int interval_start;
    public int interval_end;
    public float move_speed;
    public int flags;
    public float rarity;
    public int sync_point;
    public Vector3 extent_min = Vector3.Zero;
    public Vector3 extent_max = Vector3.Zero;
    public float extent_radius;
}

/// <summary>MDX 几何集（旧 GDS Geoset 类的 C# 等价物）。</summary>
public partial class W3MdxGeosetData : RefCounted
{
    public Vector3[] vertices = Array.Empty<Vector3>();
    public Vector3[] normals = Array.Empty<Vector3>();
    public Vector3[] vertices_war3 = Array.Empty<Vector3>();
    public Vector3[] normals_war3 = Array.Empty<Vector3>();
    public Vector2[] uvs = Array.Empty<Vector2>();
    public int[] faces = Array.Empty<int>();
    public int material_id;
    public int lod;
    public string lod_name = "";
    public byte[] vertex_groups = Array.Empty<byte>();
    public int[] matrix_group_sizes = Array.Empty<int>();
    public int[] matrix_indices = Array.Empty<int>();
    public int[] bone_indices = Array.Empty<int>();
    public float[] bone_weights = Array.Empty<float>();
    public float[] tangents = Array.Empty<float>();
}

/// <summary>骨骼（旧 GDS Bone 类的 C# 等价物）；pivot/平移/缩放保持 War3 Z-up。</summary>
public partial class W3MdxBoneData : RefCounted
{
    public string name = "";
    public int object_id = -1;
    public int parent_id = -1;
    public Vector3 pivot = Vector3.Zero;
    public int[] translation_times = Array.Empty<int>();
    public Vector3[] translation_values = Array.Empty<Vector3>();
    public int[] rotation_times = Array.Empty<int>();
    public GArray rotation_values = new();
    public int[] scale_times = Array.Empty<int>();
    public Vector3[] scale_values = Array.Empty<Vector3>();
}

/// <summary>材质图层（旧 GDS LayerInfo 类的 C# 等价物）。</summary>
public partial class W3MdxLayerData : RefCounted
{
    public int texture_id;
    public int[] texture_ids = Array.Empty<int>();
    public int[] texture_slots = Array.Empty<int>();
    public GArray texture_track_times = new();
    public GArray texture_track_values = new();
    public int[] texture_track_global_sequences = Array.Empty<int>();
    public int filter_mode;
    public int flags;
    public float alpha = 1f;
    public int[] alpha_times = Array.Empty<int>();
    public float[] alpha_values = Array.Empty<float>();
    public int alpha_global_sequence = -1;
    public float emissive_gain = 1f;
    public Color fresnel_color = Colors.White;
    public float fresnel_opacity;
    public float fresnel_team_color;
    public int shader;
}

/// <summary>Geoset 可见性动画（旧 GDS GeosetAnim 类的 C# 等价物）。</summary>
public partial class W3MdxGeosetAnimData : RefCounted
{
    public int geoset_id = -1;
    public float static_alpha = 1f;
    public int[] alpha_times = Array.Empty<int>();
    public float[] alpha_values = Array.Empty<float>();
}

/// <summary>模型挂接点（旧 GDS Attachment 类的 C# 等价物）。</summary>
public partial class W3MdxAttachmentData : RefCounted
{
    public W3MdxBoneData? node;
    public string path = "";
    public int attachment_id;
}

/// <summary>第二代粒子发射器（旧 GDS ParticleEmitter2 类的 C# 等价物）。</summary>
public partial class W3MdxParticleEmitter2Data : RefCounted
{
    public W3MdxBoneData? node;
    public float speed;
    public float speed_variation;
    public float latitude;
    public float gravity;
    public float life_span;
    public float emission_rate;
    public float length;
    public float width;
    public int filter_mode;
    public int rows = 1;
    public int columns = 1;
    public int head_or_tail;
    public float tail_length;
    public float time_middle = 0.5f;
    public Color[] colors = Array.Empty<Color>();
    public float[] scaling = Array.Empty<float>();
    public int texture_id = -1;
    public int replaceable_id;
    public int[] visibility_times = Array.Empty<int>();
    public float[] visibility_values = Array.Empty<float>();
    public int[] emission_rate_times = Array.Empty<int>();
    public float[] emission_rate_values = Array.Empty<float>();
}

/// <summary>MDX 事件对象（旧 GDS EventObject 类的 C# 等价物）。</summary>
public partial class W3MdxEventObjectData : RefCounted
{
    public W3MdxBoneData? node;
    public int global_sequence_id = -1;
    public int[] times = Array.Empty<int>();
}

/// <summary>MDX 模型灯光（旧 GDS LightInfo 类的 C# 等价物）。</summary>
public partial class W3MdxLightData : RefCounted
{
    public W3MdxBoneData? node;
    public int type;
    public float attenuation_start;
    public float attenuation_end;
    public Color color = Colors.White;
    public float intensity = 1f;
    public Color ambient_color = Colors.White;
    public float ambient_intensity;
    public int[] intensity_times = Array.Empty<int>();
    public float[] intensity_values = Array.Empty<float>();
    public int[] visibility_times = Array.Empty<int>();
    public float[] visibility_values = Array.Empty<float>();
    public int[] color_times = Array.Empty<int>();
    public Vector3[] color_values = Array.Empty<Vector3>();
    public int[] attenuation_end_times = Array.Empty<int>();
    public float[] attenuation_end_values = Array.Empty<float>();
}
