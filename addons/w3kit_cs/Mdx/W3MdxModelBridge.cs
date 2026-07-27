using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using W3Compat.Mdx;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// MDX 字节解析桥接：真正的二进制解析全部复用 W3Compat.Core.Mdx.MdxModel（与
/// standalone 编辑器共享同一份解析实现）。本类只负责把解析结果打包成
/// GDScript 能直接消费的嵌套 Dictionary/Array 快照，供 w3_mdx_model.gd
/// 门面重建其原有的 Sequence/Geoset/Bone/... 兼容对象图。
/// 字段命名与旧 GDScript 类保持一致（snake_case key），避免下游大量调用点被迫改写。
/// </summary>
[GlobalClass]
public partial class W3MdxModelBridge : RefCounted
{
    public static GDictionary? ParseFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return ParseBytes(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    public static GDictionary? parse_file(string path) => ParseFile(path);

    public static GDictionary? ParseBytes(byte[] data)
    {
        MdxModel? model = MdxModel.LoadBytes(data);
        return model == null ? null : Snapshot(model);
    }

    public static GDictionary? parse_bytes(byte[] data) => ParseBytes(data);

    // ---- 顶层快照 --------------------------------------------------------

    private static GDictionary Snapshot(MdxModel model)
    {
        // bones 展平顺序必须与 ATCH/PRE2/EVTS/LITE 内部各自 append 的顺序完全一致，
        // 这样 attachments[i].node_index 等下标才能正确指回同一个骨骼对象。
        var boneIndex = new Dictionary<MdxNode, int>(ReferenceEqualityComparer.Instance);
        var bones = new GArray();
        for (int i = 0; i < model.Bones.Count; i++)
        {
            boneIndex[model.Bones[i]] = i;
            bones.Add(BoneDict(model.Bones[i]));
        }

        var geosetAnims = new GDictionary();
        foreach (KeyValuePair<int, MdxGeosetAnimation> pair in model.GeosetAnimations)
            geosetAnims[pair.Key] = GeosetAnimDict(pair.Value);

        return new GDictionary
        {
            ["version"] = model.Version,
            ["name"] = model.Name,
            ["sequences"] = SequencesArray(model.Sequences),
            ["global_sequences"] = ToIntArray(model.GlobalSequences),
            ["textures"] = TexturesArray(model.Textures),
            ["texture_replaceable_ids"] = TextureReplaceableIds(model.Textures),
            ["geosets"] = GeosetsArray(model.Geosets),
            ["bones"] = bones,
            ["materials"] = MaterialsArray(model.Materials),
            ["pivots"] = ToVector3ArrayRaw(model.Pivots),
            ["geoset_anims"] = geosetAnims,
            ["attachments"] = AttachmentsArray(model.Attachments, boneIndex),
            ["particle_emitters2"] = ParticleEmitters2Array(model.ParticleEmitters2, boneIndex),
            ["event_objects"] = EventObjectsArray(model.EventObjects, boneIndex),
            ["lights"] = LightsArray(model.Lights, boneIndex),
        };
    }

    private static GArray SequencesArray(List<MdxSequence> sequences)
    {
        var array = new GArray();
        foreach (MdxSequence sequence in sequences)
        {
            array.Add(new GDictionary
            {
                ["name"] = sequence.Name,
                ["interval_start"] = sequence.IntervalStart,
                ["interval_end"] = sequence.IntervalEnd,
                ["move_speed"] = sequence.MoveSpeed,
                ["flags"] = sequence.Flags,
                ["rarity"] = sequence.Rarity,
                ["sync_point"] = sequence.SyncPoint,
                // GDS 原始实现不对 extent_min/max 做 War3→Godot 轴变换（字段未被下游使用，
                // 保留原始 War3 分量以维持字节级兼容）。
                ["extent_min"] = new Vector3(sequence.ExtentMin.X, sequence.ExtentMin.Y, sequence.ExtentMin.Z),
                ["extent_max"] = new Vector3(sequence.ExtentMax.X, sequence.ExtentMax.Y, sequence.ExtentMax.Z),
                ["extent_radius"] = sequence.ExtentRadius,
            });
        }
        return array;
    }

    private static string[] TexturesArray(List<MdxTexture> textures)
    {
        var array = new string[textures.Count];
        for (int i = 0; i < textures.Count; i++)
            array[i] = textures[i].Path;
        return array;
    }

    private static int[] TextureReplaceableIds(List<MdxTexture> textures)
    {
        var array = new int[textures.Count];
        for (int i = 0; i < textures.Count; i++)
            array[i] = textures[i].ReplaceableId;
        return array;
    }

    // ---- Geoset -----------------------------------------------------------

    private static GArray GeosetsArray(List<MdxGeoset> geosets)
    {
        var array = new GArray();
        foreach (MdxGeoset geoset in geosets)
            array.Add(GeosetDict(geoset));
        return array;
    }

    private static GDictionary GeosetDict(MdxGeoset geoset)
    {
        // vertices_war3/normals_war3 保持原始 War3 Z-up（蒙皮计算用）；
        // vertices/normals 做 (x,y,z)->(x,z,y) 换算供 Godot 渲染。
        var verticesWar3 = ToVector3ArrayRaw(geoset.VerticesWar3);
        var normalsWar3 = ToVector3ArrayRaw(geoset.NormalsWar3);
        var vertices = new Vector3[geoset.VerticesWar3.Count];
        var normals = new Vector3[geoset.NormalsWar3.Count];
        for (int i = 0; i < geoset.VerticesWar3.Count; i++)
            vertices[i] = geoset.VerticesWar3[i].ToGodot();
        for (int i = 0; i < geoset.NormalsWar3.Count; i++)
            normals[i] = geoset.NormalsWar3[i].ToGodot().Normalized();

        var tangents = new float[geoset.Tangents.Count * 4];
        for (int i = 0; i < geoset.Tangents.Count; i++)
        {
            System.Numerics.Vector4 t = geoset.Tangents[i];
            Vector3 tg = new System.Numerics.Vector3(t.X, t.Y, t.Z).ToGodot().Normalized();
            tangents[i * 4] = tg.X;
            tangents[i * 4 + 1] = tg.Y;
            tangents[i * 4 + 2] = tg.Z;
            tangents[i * 4 + 3] = t.W;
        }

        return new GDictionary
        {
            ["vertices"] = vertices,
            ["normals"] = normals,
            ["vertices_war3"] = verticesWar3,
            ["normals_war3"] = normalsWar3,
            ["uvs"] = ToVector2Array(geoset.Uvs),
            ["faces"] = ToIntArray(geoset.Faces),
            ["material_id"] = geoset.MaterialId,
            ["lod"] = geoset.Lod,
            ["lod_name"] = geoset.LodName,
            ["vertex_groups"] = ToByteArray(geoset.VertexGroups),
            ["matrix_group_sizes"] = ToIntArray(geoset.MatrixGroupSizes),
            ["matrix_indices"] = ToIntArray(geoset.MatrixIndices),
            ["bone_indices"] = ToIntArray(geoset.BoneIndices),
            ["bone_weights"] = ToFloatArray(geoset.BoneWeights),
            ["tangents"] = tangents,
        };
    }

    // ---- Bone / GenericObject ----------------------------------------------

    private static GDictionary BoneDict(MdxNode node)
    {
        // 骨骼 pivot/平移/缩放全部保持 War3 Z-up 原始空间——蒙皮矩阵在此空间计算，
        // 只有最终顶点/法线才换算到 Godot（与 GDS w3_mdx_model.gd 的 Bone 类一致）。
        return new GDictionary
        {
            ["name"] = node.Name,
            ["object_id"] = node.ObjectId,
            ["parent_id"] = node.ParentId,
            ["pivot"] = ToVector3Raw(node.Pivot),
            ["translation_times"] = ToIntArray(node.Translation.Times),
            ["translation_values"] = ToVector3ArrayRaw(node.Translation.Values),
            ["rotation_times"] = ToIntArray(node.Rotation.Times),
            ["rotation_values"] = QuaternionArray(node.Rotation.Values),
            ["scale_times"] = ToIntArray(node.Scale.Times),
            ["scale_values"] = ToVector3ArrayRaw(node.Scale.Values),
        };
    }

    private static GArray QuaternionArray(List<System.Numerics.Quaternion> values)
    {
        var array = new GArray();
        foreach (System.Numerics.Quaternion q in values)
            array.Add(new Quaternion(q.X, q.Y, q.Z, q.W));
        return array;
    }

    // ---- Materials ----------------------------------------------------------

    private static GArray MaterialsArray(List<List<MdxLayer>> materials)
    {
        var array = new GArray();
        foreach (List<MdxLayer> layers in materials)
        {
            var layerArray = new GArray();
            foreach (MdxLayer layer in layers)
                layerArray.Add(LayerDict(layer));
            array.Add(layerArray);
        }
        return array;
    }

    private static GDictionary LayerDict(MdxLayer layer)
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

        return new GDictionary
        {
            ["texture_id"] = layer.TextureId,
            ["texture_ids"] = ToIntArray(layer.TextureIds),
            ["texture_slots"] = ToIntArray(layer.TextureSlots),
            ["texture_track_times"] = textureTrackTimes,
            ["texture_track_values"] = textureTrackValues,
            ["texture_track_global_sequences"] = textureTrackGlobalSequences,
            ["filter_mode"] = layer.FilterMode,
            ["flags"] = layer.Flags,
            ["alpha"] = layer.Alpha,
            ["alpha_times"] = ToIntArray(layer.AlphaTrack.Times),
            ["alpha_values"] = ToFloatArray(layer.AlphaTrack.Values),
            ["alpha_global_sequence"] = layer.AlphaTrack.GlobalSequenceId,
            ["emissive_gain"] = layer.EmissiveGain,
            ["shader"] = layer.Shader,
        };
    }

    private static GDictionary GeosetAnimDict(MdxGeosetAnimation animation)
    {
        return new GDictionary
        {
            ["geoset_id"] = animation.GeosetId,
            ["static_alpha"] = animation.StaticAlpha,
            ["alpha_times"] = ToIntArray(animation.Alpha.Times),
            ["alpha_values"] = ToFloatArray(animation.Alpha.Values),
        };
    }

    // ---- Attachments / particles / events / lights ------------------------

    private static GArray AttachmentsArray(List<MdxAttachment> attachments, Dictionary<MdxNode, int> boneIndex)
    {
        var array = new GArray();
        foreach (MdxAttachment attachment in attachments)
        {
            array.Add(new GDictionary
            {
                ["node_index"] = boneIndex.GetValueOrDefault(attachment.Node, -1),
                ["path"] = attachment.Path,
                ["attachment_id"] = attachment.AttachmentId,
            });
        }
        return array;
    }

    private static GArray ParticleEmitters2Array(List<MdxParticleEmitter2> emitters, Dictionary<MdxNode, int> boneIndex)
    {
        var array = new GArray();
        foreach (MdxParticleEmitter2 emitter in emitters)
        {
            var colors = new Color[emitter.Colors.Count];
            for (int i = 0; i < emitter.Colors.Count; i++)
            {
                System.Numerics.Vector3 c = emitter.Colors[i];
                float a = i < emitter.ColorAlphas.Count ? emitter.ColorAlphas[i] : 1f;
                colors[i] = new Color(c.X, c.Y, c.Z, a);
            }
            array.Add(new GDictionary
            {
                ["node_index"] = boneIndex.GetValueOrDefault(emitter.Node, -1),
                ["speed"] = emitter.Speed,
                ["speed_variation"] = emitter.SpeedVariation,
                ["latitude"] = emitter.Latitude,
                ["gravity"] = emitter.Gravity,
                ["life_span"] = emitter.LifeSpan,
                ["emission_rate"] = emitter.EmissionRate,
                ["length"] = emitter.Length,
                ["width"] = emitter.Width,
                ["filter_mode"] = emitter.FilterMode,
                ["rows"] = emitter.Rows,
                ["columns"] = emitter.Columns,
                ["head_or_tail"] = emitter.HeadOrTail,
                ["tail_length"] = emitter.TailLength,
                ["time_middle"] = emitter.TimeMiddle,
                ["colors"] = colors,
                ["scaling"] = ToFloatArray(emitter.Scaling),
                ["texture_id"] = emitter.TextureId,
                ["replaceable_id"] = emitter.ReplaceableId,
                ["visibility_times"] = ToIntArray(emitter.Visibility.Times),
                ["visibility_values"] = ToFloatArray(emitter.Visibility.Values),
                ["emission_rate_times"] = ToIntArray(emitter.EmissionRateTrack.Times),
                ["emission_rate_values"] = ToFloatArray(emitter.EmissionRateTrack.Values),
            });
        }
        return array;
    }

    private static GArray EventObjectsArray(List<MdxEventObject> events, Dictionary<MdxNode, int> boneIndex)
    {
        var array = new GArray();
        foreach (MdxEventObject item in events)
        {
            array.Add(new GDictionary
            {
                ["node_index"] = boneIndex.GetValueOrDefault(item.Node, -1),
                ["global_sequence_id"] = item.GlobalSequenceId,
                ["times"] = ToIntArray(item.Times),
            });
        }
        return array;
    }

    private static GArray LightsArray(List<MdxLight> lights, Dictionary<MdxNode, int> boneIndex)
    {
        var array = new GArray();
        foreach (MdxLight light in lights)
        {
            array.Add(new GDictionary
            {
                ["node_index"] = boneIndex.GetValueOrDefault(light.Node, -1),
                ["type"] = light.Type,
                ["attenuation_start"] = light.AttenuationStart,
                ["attenuation_end"] = light.AttenuationEnd,
                ["color"] = new Color(light.Color.X, light.Color.Y, light.Color.Z, 1f),
                ["intensity"] = light.Intensity,
                ["ambient_color"] = new Color(light.AmbientColor.X, light.AmbientColor.Y, light.AmbientColor.Z, 1f),
                ["ambient_intensity"] = light.AmbientIntensity,
                ["intensity_times"] = ToIntArray(light.IntensityTrack.Times),
                ["intensity_values"] = ToFloatArray(light.IntensityTrack.Values),
                ["visibility_times"] = ToIntArray(light.VisibilityTrack.Times),
                ["visibility_values"] = ToFloatArray(light.VisibilityTrack.Values),
                ["color_times"] = ToIntArray(light.ColorTrack.Times),
                ["color_values"] = ToVector3ArrayRaw(light.ColorTrack.Values),
                ["attenuation_end_times"] = ToIntArray(light.AttenuationEndTrack.Times),
                ["attenuation_end_values"] = ToFloatArray(light.AttenuationEndTrack.Values),
            });
        }
        return array;
    }

    // ---- 基础类型转换 --------------------------------------------------------
    // 注意：Godot C# 绑定中没有独立的 PackedInt32Array 等结构体类型可供普通字段声明，
    // 引擎会在编组（marshaling）阶段自动把标准 C# 数组（int[]/float[]/...）转换为
    // 对应的 Packed*Array，因此这里统一改用标准数组类型。

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

    /// <summary>原始分量直拷贝，不做 War3→Godot 轴变换（骨骼/pivot/颜色轨道用）。</summary>
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

/// <summary>War3(Z-up) 坐标 → Godot(Y-up) 的换算辅助：与 MdxModel.ToGodot 等价，
/// 但直接产出 Godot.Vector3，供本文件的快照构建代码使用。</summary>
internal static class MdxVectorGodotExtensions
{
    public static Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Z, v.Y);
}
