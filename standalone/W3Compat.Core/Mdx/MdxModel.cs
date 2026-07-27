using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace W3Compat.Mdx;

/// <summary>与 Godot 无关的 Warcraft III MDX 数据模型及二进制读取器。</summary>
public sealed class MdxModel
{
    public const int MaxElements = 2_000_000;
    public const int MaxTracks = 1_000_000;
    public const int MaxLayers = 4_096;

    /// <summary>MDX 版本，经典模型通常是 800。</summary>
    public int Version { get; internal set; } = 800;
    public string Name { get; internal set; } = string.Empty;
    public List<MdxSequence> Sequences { get; } = [];
    public List<int> GlobalSequences { get; } = [];
    public List<MdxGeoset> Geosets { get; } = [];
    public List<MdxNode> Bones { get; } = [];
    public List<MdxTexture> Textures { get; } = [];
    public List<List<MdxLayer>> Materials { get; } = [];
    public List<Vector3> Pivots { get; } = [];
    public Dictionary<int, MdxGeosetAnimation> GeosetAnimations { get; } = [];
    public List<MdxAttachment> Attachments { get; } = [];
    public List<MdxParticleEmitter2> ParticleEmitters2 { get; } = [];
    public List<MdxEventObject> EventObjects { get; } = [];
    public List<MdxLight> Lights { get; } = [];

    /// <summary>读取 MDX；数据无效或不是 MDLX 时返回 null。</summary>
    public static MdxModel? LoadBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || FourCc(bytes[..4]) != "MDLX")
            return null;

        var model = new MdxModel();
        int pos = 4;
        while (pos + 8 <= bytes.Length)
        {
            string tag = FourCc(bytes.Slice(pos, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(pos + 4)..]);
            pos += 8;
            if (size < 0 || size > bytes.Length - pos)
                break;
            ReadOnlySpan<byte> chunk = bytes.Slice(pos, size);
            pos += size;
            switch (tag)
            {
                case "VERS" when chunk.Length >= 4: model.Version = ReadI32(chunk, 0); break;
                case "MODL" when chunk.Length >= 80: model.Name = FixedString(chunk[..80]); break;
                case "SEQS": ParseSequences(model, chunk); break;
                case "GLBS": ParseGlobalSequences(model, chunk); break;
                case "GEOS": ParseGeosets(model, chunk); break;
                case "GEOA": ParseGeosetAnimations(model, chunk); break;
                case "TEXS": ParseTextures(model, chunk); break;
                case "MTLS": ParseMaterials(model, chunk); break;
                case "BONE": ParseGenericNodes(model, chunk, true); break;
                case "HELP": ParseGenericNodes(model, chunk, false); break;
                case "ATCH": ParseAttachments(model, chunk); break;
                case "PRE2": ParseParticleEmitters2(model, chunk); break;
                case "EVTS": ParseEventObjects(model, chunk); break;
                case "LITE": ParseLights(model, chunk); break;
                case "PIVT": ParsePivots(model, chunk); break;
            }
        }

        foreach (MdxNode bone in model.Bones)
        {
            if (bone.ObjectId >= 0 && bone.ObjectId < model.Pivots.Count)
                bone.Pivot = model.Pivots[bone.ObjectId];
        }
        foreach (MdxGeoset geoset in model.Geosets)
            FinalizeSkin(geoset);
        return model;
    }

    /// <summary>从本地文件读取 MDX；I/O 失败返回 null。</summary>
    public static MdxModel? LoadFile(string path)
    {
        try { return File.Exists(path) ? LoadBytes(File.ReadAllBytes(path)) : null; }
        catch { return null; }
    }

    /// <summary>按 Warsmash 的前缀匹配规则查找动画。</summary>
    public int FindSequence(string query)
    {
        string needle = query.Trim().ToLowerInvariant();
        for (int i = 0; i < Sequences.Count; i++)
        {
            string name = Sequences[i].Name.ToLowerInvariant();
            if (name == needle || name.StartsWith(needle, StringComparison.Ordinal))
                return i;
        }
        return Sequences.FindIndex(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>选择第一个普通 Stand；没有时回退到首个匹配动画。</summary>
    public int FindIdleSequence()
    {
        int fallback = -1;
        for (int i = 0; i < Sequences.Count; i++)
        {
            string primary = Sequences[i].Name.Split('-')[0].Trim();
            if (!primary.Equals("stand", StringComparison.OrdinalIgnoreCase))
                continue;
            if (fallback < 0)
                fallback = i;
            if (MathF.Abs(Sequences[i].Rarity) < 0.0001f)
                return i;
        }
        return fallback >= 0 ? fallback : (FindSequence("Stand") is int found && found >= 0 ? found : (Sequences.Count > 0 ? 0 : -1));
    }

    /// <summary>取得指定 geoset 在某时刻的可见 alpha。</summary>
    public float GeosetAlphaAt(int geosetId, int timeMs)
    {
        if (!GeosetAnimations.TryGetValue(geosetId, out MdxGeosetAnimation? animation))
            return 1f;
        return MdxAnimationEvaluator.SampleFloat(animation.Alpha, timeMs, animation.StaticAlpha);
    }

    /// <summary>材质图层是否带有随时间变化的 alpha/贴图轨道（不限定具体动画片段）。</summary>
    public bool HasLayerAnimation()
    {
        foreach (List<MdxLayer> materialLayers in Materials)
            foreach (MdxLayer layer in materialLayers)
            {
                if (layer.AlphaTrack.Times.Count > 0)
                    return true;
                foreach (MdxTrack<int> track in layer.TextureTracks)
                    if (track.Times.Count > 0)
                        return true;
            }
        return false;
    }

    /// <summary>材质图层动画轨道是否在指定动画片段区间内实际生效。</summary>
    public bool HasLayerAnimationForSequence(int sequenceIndex)
    {
        if (sequenceIndex < 0 || sequenceIndex >= Sequences.Count)
            return false;
        MdxSequence sequence = Sequences[sequenceIndex];
        foreach (List<MdxLayer> materialLayers in Materials)
            foreach (MdxLayer layer in materialLayers)
            {
                if (TrackRunsInSequence(layer.AlphaTrack.Times, layer.AlphaTrack.GlobalSequenceId, sequence))
                    return true;
                foreach (MdxTrack<int> track in layer.TextureTracks)
                    if (TrackRunsInSequence(track.Times, track.GlobalSequenceId, sequence))
                        return true;
            }
        return false;
    }

    public bool HasIdleLayerAnimation() => HasLayerAnimationForSequence(FindIdleSequence());

    private bool TrackRunsInSequence(List<int> times, int globalSequenceId, MdxSequence sequence)
    {
        if (times.Count == 0)
            return false;
        if (globalSequenceId >= 0 && globalSequenceId < GlobalSequences.Count)
            return true;
        foreach (int time in times)
            if (time >= sequence.IntervalStart && time <= sequence.IntervalEnd)
                return true;
        return false;
    }

    /// <summary>指定动画片段区间内是否有骨骼平移/旋转/缩放关键帧。</summary>
    public bool HasSequenceBoneMotion(int sequenceIndex)
    {
        if (sequenceIndex < 0 || sequenceIndex >= Sequences.Count)
            return false;
        MdxSequence sequence = Sequences[sequenceIndex];
        foreach (MdxNode bone in Bones)
            foreach (List<int> times in new[] { bone.Translation.Times, bone.Rotation.Times, bone.Scale.Times })
                foreach (int time in times)
                    if (time >= sequence.IntervalStart && time <= sequence.IntervalEnd)
                        return true;
        return false;
    }

    public bool HasIdleBoneMotion() => HasSequenceBoneMotion(FindIdleSequence());

    /// <summary>MDX 中常用的可替换纹理相对路径。</summary>
    public static string ReplaceableRelativePath(int id) => id switch
    {
        1 => "ReplaceableTextures/TeamColor/TeamColor00.blp",
        2 => "ReplaceableTextures/TeamGlow/TeamGlow00.blp",
        11 => "ReplaceableTextures/Cliff/Cliff0.blp",
        31 => "ReplaceableTextures/LordaeronTree/LordaeronSummerTree.blp",
        32 => "ReplaceableTextures/AshenvaleTree/AshenTree.blp",
        33 => "ReplaceableTextures/BarrensTree/BarrensTree.blp",
        34 => "ReplaceableTextures/NorthrendTree/NorthTree.blp",
        35 => "ReplaceableTextures/Mushroom/MushroomTree.blp",
        36 => "ReplaceableTextures/RuinsTree/RuinsTree.blp",
        37 => "ReplaceableTextures/OutlandMushroomTree/MushroomTree.blp",
        _ => string.Empty,
    };

    /// <summary>War3 Z-up 到 Godot Y-up 的坐标置换。</summary>
    public static Vector3 ToGodot(Vector3 value) => new(value.X, value.Z, value.Y);

    private static void ParseSequences(MdxModel model, ReadOnlySpan<byte> data)
    {
        for (int pos = 0; pos + 132 <= data.Length; pos += 132)
        {
            model.Sequences.Add(new MdxSequence
            {
                Name = FixedString(data.Slice(pos, 80)),
                IntervalStart = ReadI32(data, pos + 80),
                IntervalEnd = ReadI32(data, pos + 84),
                MoveSpeed = ReadF32(data, pos + 88),
                Flags = ReadI32(data, pos + 92),
                Rarity = ReadF32(data, pos + 96),
                SyncPoint = ReadI32(data, pos + 100),
                ExtentMin = ReadVector3(data, pos + 104),
                ExtentMax = ReadVector3(data, pos + 116),
                ExtentRadius = ReadF32(data, pos + 128),
            });
        }
    }

    private static void ParseGlobalSequences(MdxModel model, ReadOnlySpan<byte> data)
    {
        for (int pos = 0; pos + 4 <= data.Length; pos += 4)
            model.GlobalSequences.Add(ReadI32(data, pos));
    }

    private static void ParseTextures(MdxModel model, ReadOnlySpan<byte> data)
    {
        for (int pos = 0; pos + 268 <= data.Length; pos += 268)
        {
            model.Textures.Add(new MdxTexture
            {
                ReplaceableId = ReadI32(data, pos),
                Path = FixedString(data.Slice(pos + 4, 260)).Replace('\\', '/'),
            });
        }
    }

    private static void ParseGeosetAnimations(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 28 <= data.Length)
        {
            int size = ReadI32(data, pos);
            int end = pos + size;
            if (size < 28 || end > data.Length)
                break;
            var animation = new MdxGeosetAnimation
            {
                StaticAlpha = ReadF32(data, pos + 4),
                GeosetId = ReadI32(data, pos + 24),
            };
            int cursor = pos + 28;
            while (cursor + 16 <= end)
            {
                string tag = FourCc(data.Slice(cursor, 4));
                cursor += 4;
                if (tag == "KGAO")
                    animation.Alpha = ReadFloatTrack(data, ref cursor, end);
                else if (tag == "KGAC")
                    SkipTrack(data, ref cursor, end, 12);
                else
                    break;
            }
            if (animation.GeosetId >= 0)
                model.GeosetAnimations[animation.GeosetId] = animation;
            pos = end;
        }
    }

    private static void ParseMaterials(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 12 <= data.Length)
        {
            int size = ReadI32(data, pos);
            int end = pos + size;
            if (size < 12 || end > data.Length)
                break;
            int cursor = pos + 12;
            string shaderName = string.Empty;
            if (model.Version is 900 or 1000 && cursor + 80 <= end)
            {
                shaderName = FixedString(data.Slice(cursor, 80));
                cursor += 80;
            }
            var layers = new List<MdxLayer>();
            if (cursor + 8 <= end && FourCc(data.Slice(cursor, 4)) == "LAYS")
            {
                int count = Math.Clamp(ReadI32(data, cursor + 4), 0, MaxLayers);
                cursor += 8;
                MdxLayer? mergedHd = string.IsNullOrEmpty(shaderName) ? null : new MdxLayer { Shader = 1 };
                for (int i = 0; i < count && cursor + 4 <= end; i++)
                {
                    int layerStart = cursor;
                    int layerSize = ReadI32(data, cursor);
                    int layerEnd = Math.Min(layerStart + layerSize, end);
                    if (layerSize < 16 || layerEnd <= layerStart + 4)
                        break;
                    cursor += 4;
                    var layer = new MdxLayer
                    {
                        FilterMode = ReadI32(data, cursor),
                        Flags = ReadI32(data, cursor + 4),
                        TextureId = ReadI32(data, cursor + 8),
                    };
                    cursor += 12;
                    if (cursor + 12 <= layerEnd)
                    {
                        cursor += 8; // 纹理动画和坐标集
                        layer.Alpha = ReadF32(data, cursor);
                        cursor += 4;
                    }
                    if (model.Version > 800 && cursor + 4 <= layerEnd)
                    {
                        layer.EmissiveGain = ReadF32(data, cursor);
                        cursor += 4;
                    }
                    if (model.Version > 900 && cursor + 20 <= layerEnd)
                        cursor += 20; // Fresnel 由 Godot 适配器的后续 shader 路径处理

                    if (model.Version >= 1100 && cursor + 8 <= layerEnd)
                    {
                        layer.Shader = ReadI32(data, cursor);
                        int textureCount = Math.Clamp(ReadI32(data, cursor + 4), 0, 64);
                        cursor += 8;
                        for (int slot = 0; slot < textureCount && cursor + 8 <= layerEnd; slot++)
                        {
                            layer.TextureIds.Add(ReadI32(data, cursor));
                            layer.TextureSlots.Add(ReadI32(data, cursor + 4));
                            cursor += 8;
                            if (cursor + 4 <= layerEnd && FourCc(data.Slice(cursor, 4)) == "KMTF")
                            {
                                cursor += 4;
                                layer.TextureTracks.Add(ReadIntTrack(data, ref cursor, layerEnd));
                            }
                            else
                                layer.TextureTracks.Add(new MdxTrack<int>());
                        }
                    }
                    else
                    {
                        layer.TextureIds.Add(layer.TextureId);
                        layer.TextureSlots.Add(0);
                        if (mergedHd != null)
                        {
                            mergedHd.TextureIds.Add(layer.TextureId);
                            mergedHd.TextureSlots.Add(i);
                        }
                    }
                    ParseLayerTracks(data, ref cursor, layerEnd, layer);
                    if (mergedHd != null)
                    {
                        if (i == 0)
                        {
                            mergedHd.FilterMode = layer.FilterMode;
                            mergedHd.Flags = layer.Flags;
                            mergedHd.TextureId = layer.TextureId;
                            mergedHd.Alpha = layer.Alpha;
                            mergedHd.AlphaTrack = layer.AlphaTrack;
                            mergedHd.EmissiveGain = layer.EmissiveGain;
                        }
                        mergedHd.TextureTracks.AddRange(layer.TextureTracks);
                    }
                    else
                        layers.Add(layer);
                    cursor = layerEnd;
                }
                if (mergedHd != null)
                    layers.Add(mergedHd);
            }
            model.Materials.Add(layers);
            pos = end;
        }
    }

    private static void ParseLayerTracks(ReadOnlySpan<byte> data, ref int cursor, int end, MdxLayer layer)
    {
        while (cursor + 4 <= end)
        {
            string tag = FourCc(data.Slice(cursor, 4));
            cursor += 4;
            switch (tag)
            {
                case "KMTA": layer.AlphaTrack = ReadFloatTrack(data, ref cursor, end); break;
                case "KMTF": layer.TextureTracks.Add(ReadIntTrack(data, ref cursor, end)); break;
                case "KMTE":
                case "KFCA":
                case "KFTC": _ = ReadFloatTrack(data, ref cursor, end); break;
                case "KFC3": SkipTrack(data, ref cursor, end, 12); break;
                default: cursor = end; break;
            }
        }
    }

    private static void ParseGenericNodes(MdxModel model, ReadOnlySpan<byte> data, bool isBone)
    {
        int pos = 0;
        while (pos + 96 <= data.Length)
        {
            MdxNode? node = ReadNode(data, ref pos, data.Length);
            if (node == null)
                break;
            if (isBone && pos + 8 <= data.Length)
                pos += 8; // geosetId、geosetAnimId
            model.Bones.Add(node);
        }
    }

    private static MdxNode? ReadNode(ReadOnlySpan<byte> data, ref int pos, int limit)
    {
        if (pos + 96 > limit)
            return null;
        int start = pos;
        int size = ReadI32(data, pos);
        int end = start + size;
        if (size < 96 || end > limit)
            return null;
        var node = new MdxNode
        {
            Name = FixedString(data.Slice(pos + 4, 80)),
            ObjectId = ReadI32(data, pos + 84),
            ParentId = ReadI32(data, pos + 88),
        };
        pos += 96;
        while (pos + 16 <= end)
        {
            string tag = FourCc(data.Slice(pos, 4));
            pos += 4;
            switch (tag)
            {
                case "KGTR": node.Translation = ReadVectorTrack(data, ref pos, end); break;
                case "KGRT": node.Rotation = ReadQuaternionTrack(data, ref pos, end); break;
                case "KGSC": node.Scale = ReadVectorTrack(data, ref pos, end); break;
                default: pos = end; break;
            }
        }
        pos = end;
        return node;
    }

    private static void ParseAttachments(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int start = pos;
            int size = ReadI32(data, pos);
            int end = start + size;
            if (size < 4 || end > data.Length)
                break;
            // HiveWE mdx_reader.cpp read_ATCH 与 Warsmash MdlxAttachment 都证实：外层
            // inclusive_size 之后紧跟的是 Node 自己的 inclusive_size（双重 size 字段）。
            // 之前直接把 pos 停在外层 size 处调用 ReadNode，会把外层 size 误读成
            // Node.size，导致 name/object_id/parent_id 全部错位、tracks 循环读到垃圾
            // tag 而提前经 default 分支把 pos 跳到 end——外层校验必然失败，
            // 因而这个 Attachment 会被整体丢弃。
            pos += 4;
            MdxNode? node = ReadNode(data, ref pos, end);
            if (node != null && pos + 264 <= end)
            {
                var attachment = new MdxAttachment { Node = node, Path = FixedString(data.Slice(pos, 256)).Replace('\\', '/'), AttachmentId = ReadI32(data, pos + 260) };
                model.Attachments.Add(attachment);
                model.Bones.Add(node);
            }
            pos = end;
        }
    }

    private static void ParseParticleEmitters2(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int start = pos;
            int size = ReadI32(data, pos);
            int end = start + size;
            if (size < 4 || end > data.Length)
                break;
            // 同 ParseAttachments：外层 inclusive_size 后面才是 Node 自己的 inclusive_size，
            // 必须先跳过外层 size 的 4 字节，否则 ReadNode 会把外层 size 误当成节点 size。
            pos += 4;
            MdxNode? node = ReadNode(data, ref pos, end);
            if (node == null || pos + 171 > end) { pos = end; continue; }
            var emitter = new MdxParticleEmitter2 { Node = node };
            emitter.Speed = ReadF32(data, pos); emitter.SpeedVariation = ReadF32(data, pos + 4); emitter.Latitude = ReadF32(data, pos + 8);
            emitter.Gravity = ReadF32(data, pos + 12); emitter.LifeSpan = ReadF32(data, pos + 16); emitter.EmissionRate = ReadF32(data, pos + 20);
            emitter.Length = ReadF32(data, pos + 24); emitter.Width = ReadF32(data, pos + 28); emitter.FilterMode = ReadI32(data, pos + 32);
            emitter.Rows = Math.Max(1, ReadI32(data, pos + 36)); emitter.Columns = Math.Max(1, ReadI32(data, pos + 40));
            emitter.HeadOrTail = ReadI32(data, pos + 44); emitter.TailLength = ReadF32(data, pos + 48); emitter.TimeMiddle = ReadF32(data, pos + 52);
            // 3 个渐变关键色 (start/mid/end)，每个是 Vector3 RGB + 独立字节 alpha。
            for (int c = 0; c < 3; c++)
                emitter.Colors.Add(ReadVector3(data, pos + 56 + c * 12));
            for (int c = 0; c < 3; c++)
                emitter.ColorAlphas.Add(data[pos + 92 + c] / 255f);
            for (int s = 0; s < 3; s++)
                emitter.Scaling.Add(ReadF32(data, pos + 95 + s * 4));
            emitter.TextureId = ReadI32(data, pos + 155); emitter.ReplaceableId = ReadI32(data, pos + 167);
            pos += 171;
            while (pos + 16 <= end)
            {
                string tag = FourCc(data.Slice(pos, 4));
                pos += 4;
                MdxTrack<float> track = ReadFloatTrack(data, ref pos, end);
                if (tag == "KP2V") emitter.Visibility = track;
                else if (tag == "KP2E") emitter.EmissionRateTrack = track;
            }
            model.ParticleEmitters2.Add(emitter);
            model.Bones.Add(node);
            pos = end;
        }
    }

    private static void ParseEventObjects(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 96 <= data.Length)
        {
            MdxNode? node = ReadNode(data, ref pos, data.Length);
            if (node == null || pos + 12 > data.Length || FourCc(data.Slice(pos, 4)) != "KEVT")
                break;
            int count = Math.Clamp(ReadI32(data, pos + 4), 0, MaxTracks);
            int global = ReadI32(data, pos + 8);
            pos += 12;
            var item = new MdxEventObject { Node = node, GlobalSequenceId = global };
            for (int i = 0; i < count && pos + 4 <= data.Length; i++, pos += 4)
                item.Times.Add(ReadI32(data, pos));
            model.EventObjects.Add(item);
            model.Bones.Add(node);
        }
    }

    private static void ParseLights(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int start = pos;
            int size = ReadI32(data, pos);
            int end = start + size;
            if (size < 4 || end > data.Length)
                break;
            // 同 ParseAttachments/ParseParticleEmitters2：先跳过外层 inclusive_size。
            pos += 4;
            MdxNode? node = ReadNode(data, ref pos, end);
            if (node == null || pos + 44 > end) { pos = end; continue; }
            var light = new MdxLight
            {
                Node = node, Type = ReadI32(data, pos), AttenuationStart = ReadF32(data, pos + 4), AttenuationEnd = ReadF32(data, pos + 8),
                Color = ReadVector3(data, pos + 12), Intensity = ReadF32(data, pos + 24), AmbientColor = ReadVector3(data, pos + 28),
                AmbientIntensity = ReadF32(data, pos + 40),
            };
            pos += 44;
            while (pos + 16 <= end)
            {
                string tag = FourCc(data.Slice(pos, 4));
                pos += 4;
                if (tag == "KLAC") light.ColorTrack = ReadVectorTrack(data, ref pos, end);
                else if (tag is "KLAI" or "KLAV" or "KLAE")
                {
                    MdxTrack<float> track = ReadFloatTrack(data, ref pos, end);
                    if (tag == "KLAI") light.IntensityTrack = track;
                    else if (tag == "KLAV") light.VisibilityTrack = track;
                    else light.AttenuationEndTrack = track;
                }
                else
                    break;
            }
            model.Lights.Add(light);
            model.Bones.Add(node);
            pos = end;
        }
    }

    private static void ParsePivots(MdxModel model, ReadOnlySpan<byte> data)
    {
        for (int pos = 0; pos + 12 <= data.Length; pos += 12)
            model.Pivots.Add(ReadVector3(data, pos));
    }

    private static void ParseGeosets(MdxModel model, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int start = pos;
            int size = ReadI32(data, pos);
            int end = start + size;
            if (size < 4 || end > data.Length)
                break;
            pos += 4;
            var geoset = new MdxGeoset();
            while (pos + 8 <= end)
            {
                string tag = FourCc(data.Slice(pos, 4));
                pos += 4;
                int count = ReadBoundedCount(data, ref pos, end, tag == "PVTX" ? 2 : tag is "GNDX" ? 1 : 4, MaxElements);
                switch (tag)
                {
                    case "VRTX":
                        for (int i = 0; i < count; i++) { Vector3 v = ReadVector3(data, pos); pos += 12; geoset.VerticesWar3.Add(v); }
                        break;
                    case "NRMS":
                        for (int i = 0; i < count; i++) { geoset.NormalsWar3.Add(Vector3.Normalize(ReadVector3(data, pos))); pos += 12; }
                        break;
                    // PTYP（面类型分组）/PCNT（每组面数）：HiveWE mdx_reader.cpp read_GEOS 与
                    // Warsmash MdlxGeoset.readMdx 都确认它们是 VRTX/NRMS 之后、PVTX 之前的固定
                    // 顺序字段。原实现缺失这两个 case，落入 default 导致 pos=end 提前中断，
                    // 使得后面的 PVTX（faces）永远读不到——每个几何体因此被判定为空并丢弃。
                    case "PTYP":
                    case "PCNT":
                        pos += count * 4;
                        break;
                    case "PVTX":
                        for (int i = 0; i < count; i++, pos += 2) geoset.Faces.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]));
                        break;
                    case "GNDX":
                        for (int i = 0; i < count && pos < end; i++, pos++) geoset.VertexGroups.Add(data[pos]);
                        break;
                    case "MTGC":
                        for (int i = 0; i < count; i++, pos += 4) geoset.MatrixGroupSizes.Add(ReadI32(data, pos));
                        break;
                    case "MATS":
                        for (int i = 0; i < count; i++, pos += 4) geoset.MatrixIndices.Add(ReadI32(data, pos));
                        if (pos + 12 > end) { pos = end; break; }
                        geoset.MaterialId = ReadI32(data, pos);
                        pos += 12;
                        if (model.Version > 800 && pos + 84 <= end) { geoset.Lod = ReadI32(data, pos); geoset.LodName = FixedString(data.Slice(pos + 4, 80)); pos += 84; }
                        pos = Math.Min(end, pos + 28);
                        if (pos + 4 <= end)
                        {
                            int extents = Math.Clamp(ReadI32(data, pos), 0, 4096);
                            pos = Math.Min(end, pos + 4 + extents * 28);
                        }
                        ReadExtendedGeosetData(data, ref pos, end, geoset, model.Version);
                        break;
                    case "UVAS":
                        for (int set = 0; set < count && pos + 8 <= end; set++)
                        {
                            if (FourCc(data.Slice(pos, 4)) != "UVBS") { pos = end; break; }
                            pos += 4;
                            int uvCount = ReadBoundedCount(data, ref pos, end, 8, MaxElements);
                            for (int i = 0; i < uvCount; i++, pos += 8)
                                if (set == 0) geoset.Uvs.Add(new Vector2(ReadF32(data, pos), ReadF32(data, pos + 4)));
                        }
                        break;
                    default:
                        pos = end;
                        break;
                }
            }
            if (geoset.VerticesWar3.Count > 0 && geoset.Faces.Count > 0)
                model.Geosets.Add(geoset);
            pos = end;
        }
    }

    private static void ReadExtendedGeosetData(ReadOnlySpan<byte> data, ref int pos, int end, MdxGeoset geoset, int version)
    {
        if (version <= 800 || pos + 4 > end)
            return;
        if (FourCc(data.Slice(pos, 4)) == "TANG")
        {
            pos += 4;
            int count = ReadBoundedCount(data, ref pos, end, 16, MaxElements);
            for (int i = 0; i < count; i++, pos += 16)
                geoset.Tangents.Add(new Vector4(ReadF32(data, pos), ReadF32(data, pos + 4), ReadF32(data, pos + 8), ReadF32(data, pos + 12)));
        }
        if (pos + 8 <= end && FourCc(data.Slice(pos, 4)) == "SKIN")
        {
            int bytes = Math.Clamp(ReadI32(data, pos + 4), 0, end - pos - 8);
            pos += 8;
            for (int i = 0; i + 7 < bytes; i += 8)
            {
                for (int j = 0; j < 4; j++) geoset.BoneIndices.Add(data[pos + i + j]);
                for (int j = 0; j < 4; j++) geoset.BoneWeights.Add(data[pos + i + 4 + j] / 255f);
            }
            pos += bytes;
        }
    }

    private static void FinalizeSkin(MdxGeoset geoset)
    {
        int vertices = geoset.VerticesWar3.Count;
        if (geoset.BoneIndices.Count >= vertices * 4 && geoset.BoneWeights.Count >= vertices * 4)
            return;
        int offset = 0;
        var groups = new List<List<int>>();
        foreach (int size in geoset.MatrixGroupSizes)
        {
            var group = new List<int>();
            for (int i = 0; i < size && offset < geoset.MatrixIndices.Count; i++, offset++)
                group.Add(geoset.MatrixIndices[offset]);
            groups.Add(group);
        }
        for (int vertex = 0; vertex < vertices; vertex++)
        {
            List<int> group = vertex < geoset.VertexGroups.Count && geoset.VertexGroups[vertex] < groups.Count ? groups[geoset.VertexGroups[vertex]] : [];
            int count = Math.Min(4, group.Count);
            for (int i = 0; i < 4; i++)
            {
                geoset.BoneIndices.Add(i < count ? group[i] : -1);
                geoset.BoneWeights.Add(count > 0 && i < count ? 1f / count : 0f);
            }
        }
    }

    private static MdxTrack<Vector3> ReadVectorTrack(ReadOnlySpan<byte> data, ref int pos, int limit) =>
        ReadTrack(data, ref pos, limit, 12, ReadVector3);
    private static MdxTrack<Quaternion> ReadQuaternionTrack(ReadOnlySpan<byte> data, ref int pos, int limit) =>
        ReadTrack(data, ref pos, limit, 16, static (d, p) => new Quaternion(ReadF32(d, p), ReadF32(d, p + 4), ReadF32(d, p + 8), ReadF32(d, p + 12)));
    private static MdxTrack<float> ReadFloatTrack(ReadOnlySpan<byte> data, ref int pos, int limit) =>
        ReadTrack(data, ref pos, limit, 4, static (d, p) => ReadF32(d, p));
    private static MdxTrack<int> ReadIntTrack(ReadOnlySpan<byte> data, ref int pos, int limit) =>
        ReadTrack(data, ref pos, limit, 4, static (d, p) => ReadI32(d, p));

    // 自定义委托才能携带 ReadOnlySpan（不可用于 Func<>）
    private delegate T SpanValueReader<T>(ReadOnlySpan<byte> data, int offset);

    private static MdxTrack<T> ReadTrack<T>(ReadOnlySpan<byte> data, ref int pos, int limit, int valueBytes, SpanValueReader<T> read)
    {
        var track = new MdxTrack<T>();
        if (pos + 12 > limit) { pos = limit; return track; }
        int count = Math.Clamp(ReadI32(data, pos), 0, MaxTracks);
        int interpolation = ReadI32(data, pos + 4);
        track.GlobalSequenceId = ReadI32(data, pos + 8);
        pos += 12;
        int stride = 4 + valueBytes + (interpolation > 1 ? valueBytes * 2 : 0);
        count = Math.Min(count, Math.Max(0, (limit - pos) / stride));
        for (int i = 0; i < count; i++)
        {
            track.Times.Add(ReadI32(data, pos));
            track.Values.Add(read(data, pos + 4));
            pos += stride;
        }
        return track;
    }

    private static void SkipTrack(ReadOnlySpan<byte> data, ref int pos, int limit, int valueBytes)
    {
        if (pos + 12 > limit) { pos = limit; return; }
        int count = Math.Clamp(ReadI32(data, pos), 0, MaxTracks);
        int interpolation = ReadI32(data, pos + 4);
        pos += 12;
        int stride = 4 + valueBytes + (interpolation > 1 ? valueBytes * 2 : 0);
        pos = Math.Min(limit, pos + Math.Min(count, Math.Max(0, (limit - pos) / stride)) * stride);
    }

    private static int ReadBoundedCount(ReadOnlySpan<byte> data, ref int pos, int end, int bytesPerItem, int max)
    {
        if (pos + 4 > end) return 0;
        int count = ReadI32(data, pos);
        pos += 4;
        return count is < 0 or > MaxElements || bytesPerItem <= 0 || (long)count * bytesPerItem > end - pos ? 0 : Math.Min(count, max);
    }

    private static int ReadI32(ReadOnlySpan<byte> data, int pos) => BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
    private static float ReadF32(ReadOnlySpan<byte> data, int pos) => BitConverter.Int32BitsToSingle(ReadI32(data, pos));
    private static Vector3 ReadVector3(ReadOnlySpan<byte> data, int pos) => new(ReadF32(data, pos), ReadF32(data, pos + 4), ReadF32(data, pos + 8));
    private static string FixedString(ReadOnlySpan<byte> data) { int length = data.IndexOf((byte)0); return Encoding.UTF8.GetString(length < 0 ? data : data[..length]); }
    private static string FourCc(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data);
}

/// <summary>MDX 动画片段。</summary>
public sealed class MdxSequence { public string Name { get; init; } = ""; public int IntervalStart { get; init; } public int IntervalEnd { get; init; } public float MoveSpeed { get; init; } public int Flags { get; init; } public float Rarity { get; init; } public int SyncPoint { get; init; } public Vector3 ExtentMin { get; init; } public Vector3 ExtentMax { get; init; } public float ExtentRadius { get; init; } }
/// <summary>MDX 几何集，坐标始终保持 War3 Z-up。</summary>
public sealed class MdxGeoset { public List<Vector3> VerticesWar3 { get; } = []; public List<Vector3> NormalsWar3 { get; } = []; public List<Vector2> Uvs { get; } = []; public List<int> Faces { get; } = []; public int MaterialId { get; internal set; } public int Lod { get; internal set; } public string LodName { get; internal set; } = ""; public List<byte> VertexGroups { get; } = []; public List<int> MatrixGroupSizes { get; } = []; public List<int> MatrixIndices { get; } = []; public List<int> BoneIndices { get; } = []; public List<float> BoneWeights { get; } = []; public List<Vector4> Tangents { get; } = []; }
/// <summary>骨骼、辅助节点、特效节点共用的 GenericObject。</summary>
public sealed class MdxNode { public string Name { get; set; } = ""; public int ObjectId { get; set; } = -1; public int ParentId { get; set; } = -1; public Vector3 Pivot { get; set; } public MdxTrack<Vector3> Translation { get; set; } = new(); public MdxTrack<Quaternion> Rotation { get; set; } = new(); public MdxTrack<Vector3> Scale { get; set; } = new(); }
/// <summary>材质中的纹理项目。</summary>
public sealed class MdxTexture { public string Path { get; internal set; } = ""; public int ReplaceableId { get; internal set; } }
/// <summary>MDX 图层及其动画轨道。</summary>
public sealed class MdxLayer { public int TextureId { get; internal set; } public List<int> TextureIds { get; } = []; public List<int> TextureSlots { get; } = []; public List<MdxTrack<int>> TextureTracks { get; } = []; public int FilterMode { get; internal set; } public int Flags { get; internal set; } public float Alpha { get; internal set; } = 1f; public MdxTrack<float> AlphaTrack { get; internal set; } = new(); public float EmissiveGain { get; internal set; } = 1f; public int Shader { get; internal set; } }
/// <summary>Geoset 可见性动画。</summary>
public sealed class MdxGeosetAnimation { public int GeosetId { get; internal set; } = -1; public float StaticAlpha { get; internal set; } = 1f; public MdxTrack<float> Alpha { get; internal set; } = new(); }
/// <summary>通用 MDX 关键帧轨道。</summary>
public sealed class MdxTrack<T> { public List<int> Times { get; } = []; public List<T> Values { get; } = []; public int GlobalSequenceId { get; internal set; } = -1; }
/// <summary>模型挂接点。</summary>
public sealed class MdxAttachment { public required MdxNode Node { get; init; } public string Path { get; init; } = ""; public int AttachmentId { get; init; } }
/// <summary>第二代粒子发射器的渲染所需字段。</summary>
public sealed class MdxParticleEmitter2 { public required MdxNode Node { get; init; } public float Speed { get; internal set; } public float SpeedVariation { get; internal set; } public float Latitude { get; internal set; } public float Gravity { get; internal set; } public float LifeSpan { get; internal set; } public float EmissionRate { get; internal set; } public float Length { get; internal set; } public float Width { get; internal set; } public int FilterMode { get; internal set; } public int Rows { get; internal set; } = 1; public int Columns { get; internal set; } = 1; public int HeadOrTail { get; internal set; } public float TailLength { get; internal set; } public float TimeMiddle { get; internal set; } = 0.5f; public List<Vector3> Colors { get; } = []; public List<float> ColorAlphas { get; } = []; public List<float> Scaling { get; } = []; public int TextureId { get; internal set; } = -1; public int ReplaceableId { get; internal set; } public MdxTrack<float> Visibility { get; internal set; } = new(); public MdxTrack<float> EmissionRateTrack { get; internal set; } = new(); }
/// <summary>MDX 事件对象。</summary>
public sealed class MdxEventObject { public required MdxNode Node { get; init; } public int GlobalSequenceId { get; init; } = -1; public List<int> Times { get; } = []; }
/// <summary>MDX 模型灯光。</summary>
public sealed class MdxLight { public required MdxNode Node { get; init; } public int Type { get; init; } public float AttenuationStart { get; init; } public float AttenuationEnd { get; init; } public Vector3 Color { get; init; } = Vector3.One; public float Intensity { get; init; } = 1; public Vector3 AmbientColor { get; init; } = Vector3.One; public float AmbientIntensity { get; init; } public MdxTrack<float> IntensityTrack { get; internal set; } = new(); public MdxTrack<float> VisibilityTrack { get; internal set; } = new(); public MdxTrack<float> AttenuationEndTrack { get; internal set; } = new(); public MdxTrack<Vector3> ColorTrack { get; internal set; } = new(); }
