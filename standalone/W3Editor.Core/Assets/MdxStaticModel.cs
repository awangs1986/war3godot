using System.Buffers.Binary;
using System.Text;

namespace W3Editor.Rendering;

/// <summary>
/// Static-geometry MDX reader for the editor GL renderer: GEOS geosets with
/// per-geoset material ids, TEXS textures (incl. replaceable ids), MTLS first
/// layer per material (filter mode + diffuse texture), SEQS + GEOA so the
/// "Stand" visibility decision hides death/stump geosets. Positions stay in
/// War3 space; bones/animation are ignored (bind pose render).
/// Chunk walking mirrors addons/w3kit/w3_mdx_model.gd.
/// </summary>
public sealed class MdxStaticModel
{
    private const int MaxElements = 1_000_000;

    /// <summary>mdx-m3-viewer replaceableids.js — empty TEXS path + replaceableId → this stem + .blp.</summary>
    public static readonly Dictionary<int, string> ReplaceableStems = new()
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

    public sealed class Geoset
    {
        public List<float> Positions = new();  // xyz triplets, War3 axes
        public List<float> Normals = new();    // xyz triplets
        public List<float> Uvs = new();        // uv pairs
        public List<ushort> Faces = new();
        public int MaterialId;
        public int Lod;
        /// <summary>Index into the GEOA table (order of appearance); GEOA carries its own geoset id.</summary>
        public int GeosetIndex;
    }

    public sealed class TextureEntry
    {
        public string Path = "";
        public int ReplaceableId;
    }

    public sealed class MaterialLayer
    {
        public int FilterMode;   // 0 opaque, 1 cutout(transparent), 2 blend, 3/4 add, 5/6 modulate
        public int Flags;
        public int TextureId = -1;
        public bool Hd;          // v900/v1000 shader material or v1100 shader != 0
    }

    private sealed class GeosetAnim
    {
        public float StaticAlpha = 1f;
        public int GeosetId = -1;
        public List<int> AlphaTimes = new();
        public List<float> AlphaValues = new();
    }

    public int Version { get; private set; } = 800;
    public List<Geoset> Geosets { get; } = new();
    public List<TextureEntry> Textures { get; } = new();
    /// <summary>First layer of each MTLS material (diffuse decision only).</summary>
    public List<MaterialLayer> Materials { get; } = new();

    private readonly Dictionary<int, GeosetAnim> _geosetAnims = new();
    private readonly List<(string Name, int Start)> _sequences = new();

    /// <summary>Bounding radius around the model origin in W3 units (XY plane biased, for picking).</summary>
    public float BoundsRadius { get; private set; }
    public float BoundsMinZ { get; private set; }
    public float BoundsMaxZ { get; private set; }

    // ------------------------------------------------------------------
    // Static "Stand" visibility (hides death/stump geosets in the editor)
    // ------------------------------------------------------------------

    /// <summary>Warsmash filterSequences: primary token before '-' equals "stand"; fallback first sequence.</summary>
    private int StandStartTime()
    {
        foreach ((string name, int start) in _sequences)
        {
            string primary = name;
            int dash = primary.IndexOf('-');
            if (dash >= 0)
                primary = primary[..dash];
            if (primary.Trim().Equals("stand", StringComparison.OrdinalIgnoreCase))
                return start;
        }
        return _sequences.Count > 0 ? _sequences[0].Start : 0;
    }

    public bool GeosetVisibleAtStand(Geoset geoset)
    {
        if (!_geosetAnims.TryGetValue(geoset.GeosetIndex, out GeosetAnim? anim))
            return true;
        int time = StandStartTime();
        float alpha;
        if (anim.AlphaTimes.Count == 0)
            alpha = anim.StaticAlpha;
        else if (time <= anim.AlphaTimes[0])
            alpha = anim.AlphaValues[0];
        else if (time >= anim.AlphaTimes[^1])
            alpha = anim.AlphaValues[^1];
        else
        {
            alpha = anim.AlphaValues[^1];
            for (int i = 0; i < anim.AlphaTimes.Count - 1; i++)
            {
                if (time >= anim.AlphaTimes[i] && time <= anim.AlphaTimes[i + 1])
                {
                    alpha = anim.AlphaValues[i];
                    break;
                }
            }
        }
        return alpha > 0.01f;
    }

    public MaterialLayer? MaterialFor(Geoset geoset) =>
        Materials.Count == 0 ? null : Materials[Math.Clamp(geoset.MaterialId, 0, Materials.Count - 1)];

    /// <summary>
    /// Diffuse texture relative path for a geoset ("" = untextured), applying the
    /// replaceable table. <paramref name="destructibleTexFile"/> overrides tree
    /// replaceables (rid >= 31), mirroring w3_mdx_batcher._resolve_tex.
    /// </summary>
    public string ResolveTexturePath(Geoset geoset, string destructibleTexFile)
    {
        MaterialLayer? layer = MaterialFor(geoset);
        if (layer == null || layer.TextureId < 0 || layer.TextureId >= Textures.Count)
            return "";
        TextureEntry entry = Textures[layer.TextureId];
        if (entry.Path.Length > 0)
            return entry.Path;
        int rid = entry.ReplaceableId;
        if (destructibleTexFile.Length > 0 && rid >= 31)
        {
            string rt = destructibleTexFile.Replace('\\', '/');
            return rt.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? rt : rt + ".blp";
        }
        return ReplaceableStems.TryGetValue(rid, out string? stem) ? stem + ".blp" : "";
    }

    // ------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------

    public static MdxStaticModel? LoadFile(string path)
    {
        try
        {
            return File.Exists(path) ? LoadBytes(File.ReadAllBytes(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static MdxStaticModel? LoadBytes(byte[] data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x584C444D) // "MDLX"
            return null;
        var model = new MdxStaticModel();
        int pos = 4;
        while (pos + 8 <= data.Length)
        {
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
            pos += 8;
            if (size < 0 || pos + size > data.Length)
                break;
            var chunk = data.AsSpan(pos, size);
            switch (tag)
            {
                case 0x53524556 when size >= 4: // VERS
                    model.Version = BinaryPrimitives.ReadInt32LittleEndian(chunk);
                    break;
                case 0x534F4547: // GEOS
                    ParseGeos(model, chunk);
                    break;
                case 0x53584554: // TEXS
                    ParseTexs(model, chunk);
                    break;
                case 0x534C544D: // MTLS
                    ParseMtls(model, chunk);
                    break;
                case 0x53514553: // SEQS
                    ParseSeqs(model, chunk);
                    break;
                case 0x414F4547: // GEOA
                    ParseGeoa(model, chunk);
                    break;
            }
            pos += size;
        }
        model.ComputeBounds();
        return model.Geosets.Count > 0 ? model : null;
    }

    private void ComputeBounds()
    {
        float maxR2 = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (Geoset g in Geosets)
        {
            for (int i = 0; i < g.Positions.Count; i += 3)
            {
                float x = g.Positions[i], y = g.Positions[i + 1], z = g.Positions[i + 2];
                maxR2 = MathF.Max(maxR2, x * x + y * y);
                minZ = MathF.Min(minZ, z);
                maxZ = MathF.Max(maxZ, z);
            }
        }
        BoundsRadius = MathF.Sqrt(maxR2);
        BoundsMinZ = minZ == float.MaxValue ? 0f : minZ;
        BoundsMaxZ = maxZ == float.MinValue ? 0f : maxZ;
    }

    // ------------------------------------------------------------------
    // Chunk parsers
    // ------------------------------------------------------------------

    private static void ParseTexs(MdxStaticModel model, ReadOnlySpan<byte> chunk)
    {
        int pos = 0;
        while (pos + 268 <= chunk.Length)
        {
            int replaceable = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
            string path = FixedCString(chunk.Slice(pos + 4, 260));
            // + 4 bytes flags
            pos += 268;
            model.Textures.Add(new TextureEntry { Path = path.Replace('\\', '/'), ReplaceableId = replaceable });
        }
    }

    private static void ParseSeqs(MdxStaticModel model, ReadOnlySpan<byte> chunk)
    {
        int pos = 0;
        while (pos + 132 <= chunk.Length)
        {
            string name = FixedCString(chunk.Slice(pos, 80));
            int start = BinaryPrimitives.ReadInt32LittleEndian(chunk[(pos + 80)..]);
            model._sequences.Add((name, start));
            pos += 132;
        }
    }

    /// <summary>mdx-m3-viewer GeosetAnimation.readMdx: alpha, flags, color[3], geosetId, then KGAO/KGAC.</summary>
    private static void ParseGeoa(MdxStaticModel model, ReadOnlySpan<byte> chunk)
    {
        int pos = 0;
        while (pos + 28 <= chunk.Length)
        {
            int inclusive = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
            int end = pos + inclusive;
            if (inclusive < 28 || end > chunk.Length)
                break;
            var anim = new GeosetAnim
            {
                StaticAlpha = BitConverter.ToSingle(chunk[(pos + 4)..(pos + 8)]),
                GeosetId = BinaryPrimitives.ReadInt32LittleEndian(chunk[(pos + 24)..]),
            };
            int p = pos + 28;
            while (p + 4 <= end)
            {
                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(chunk[p..]);
                p += 4;
                if (tag == 0x4F41474B) // KGAO
                {
                    if (p + 12 > end) break;
                    int tracks = BinaryPrimitives.ReadInt32LittleEndian(chunk[p..]);
                    int interp = BinaryPrimitives.ReadInt32LittleEndian(chunk[(p + 4)..]);
                    p += 12; // tracks, interp, global seq
                    int stride = 8 + (interp > 1 ? 8 : 0);
                    tracks = Math.Clamp(tracks, 0, (end - p) / Math.Max(stride, 1));
                    for (int i = 0; i < tracks; i++)
                    {
                        anim.AlphaTimes.Add(BinaryPrimitives.ReadInt32LittleEndian(chunk[p..]));
                        anim.AlphaValues.Add(BitConverter.ToSingle(chunk[(p + 4)..(p + 8)]));
                        p += stride;
                    }
                }
                else if (tag == 0x4341474B) // KGAC (color track) — skip
                {
                    if (p + 12 > end) break;
                    int tracks = BinaryPrimitives.ReadInt32LittleEndian(chunk[p..]);
                    int interp = BinaryPrimitives.ReadInt32LittleEndian(chunk[(p + 4)..]);
                    p += 12;
                    int stride = 16 + (interp > 1 ? 24 : 0);
                    tracks = Math.Clamp(tracks, 0, (end - p) / Math.Max(stride, 1));
                    p += tracks * stride;
                }
                else
                {
                    break;
                }
            }
            if (anim.GeosetId >= 0)
                model._geosetAnims[anim.GeosetId] = anim;
            pos = end;
        }
    }

    /// <summary>First layer per material: filter mode + texture id + HD detection (w3_mdx_model._parse_mtls).</summary>
    private static void ParseMtls(MdxStaticModel model, ReadOnlySpan<byte> chunk)
    {
        int pos = 0;
        while (pos + 8 <= chunk.Length)
        {
            int inclusive = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
            int end = pos + inclusive;
            if (inclusive < 12 || end > chunk.Length)
                break;
            int p = pos + 4;
            p += 8; // priority plane, flags
            string shaderName = "";
            if ((model.Version == 900 || model.Version == 1000) && p + 80 <= end)
            {
                shaderName = FixedCString(chunk.Slice(p, 80));
                p += 80;
            }
            var material = new MaterialLayer();
            if (p + 8 <= end && BinaryPrimitives.ReadUInt32LittleEndian(chunk[p..]) == 0x5359414C) // LAYS
            {
                p += 4;
                int layerCount = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(chunk[p..]), 0, 64);
                p += 4;
                for (int layer = 0; layer < layerCount && p + 4 <= end; layer++)
                {
                    int layerInclusive = BinaryPrimitives.ReadInt32LittleEndian(chunk[p..]);
                    int layerEnd = Math.Min(p + layerInclusive, end);
                    if (layerInclusive < 16 || layerEnd <= p + 4)
                        break;
                    int lp = p + 4;
                    int filterMode = BinaryPrimitives.ReadInt32LittleEndian(chunk[lp..]);
                    int flags = BinaryPrimitives.ReadInt32LittleEndian(chunk[(lp + 4)..]);
                    int textureId = BinaryPrimitives.ReadInt32LittleEndian(chunk[(lp + 8)..]);
                    if (layer == 0)
                    {
                        material.FilterMode = filterMode;
                        material.Flags = flags;
                        material.TextureId = textureId;
                        material.Hd = shaderName.Length > 0;
                    }
                    if (model.Version >= 1100)
                    {
                        // v1100 layer prefix: filter/flags/texid/animid/coordid/alpha (24)
                        // + emissiveGain (4, v>800) + fresnel color/opacity/team (20, v>900),
                        // then shader int + texture slot list; slot 0 = diffuse.
                        lp += 24 + 4 + 20;
                        if (lp + 8 <= layerEnd)
                        {
                            int shader = BinaryPrimitives.ReadInt32LittleEndian(chunk[lp..]);
                            int texCount = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(chunk[(lp + 4)..]), 0, 64);
                            lp += 8;
                            if (layer == 0)
                            {
                                material.Hd = shader != 0;
                                if (texCount > 0 && lp + 8 <= layerEnd)
                                {
                                    int slotTexture = BinaryPrimitives.ReadInt32LittleEndian(chunk[lp..]);
                                    int slot = BinaryPrimitives.ReadInt32LittleEndian(chunk[(lp + 4)..]);
                                    if (slot == 0)
                                        material.TextureId = slotTexture;
                                }
                            }
                        }
                    }
                    p = layerEnd;
                }
            }
            model.Materials.Add(material);
            pos = end;
        }
    }

    private static void ParseGeos(MdxStaticModel model, ReadOnlySpan<byte> chunk)
    {
        int pos = 0;
        int geosetIndex = 0;
        while (pos + 4 <= chunk.Length)
        {
            int inclusive = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
            int end = pos + inclusive;
            if (inclusive < 8 || end > chunk.Length)
                break;
            pos += 4;
            var geoset = new Geoset { GeosetIndex = geosetIndex };
            while (pos + 8 <= end)
            {
                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(chunk[pos..]);
                pos += 4;
                if (tag == 0x58545256) // VRTX
                {
                    int count = ReadCount(chunk, ref pos, end, 12);
                    for (int i = 0; i < count * 3; i++)
                        geoset.Positions.Add(ReadF32(chunk, ref pos));
                }
                else if (tag == 0x534D524E) // NRMS
                {
                    int count = ReadCount(chunk, ref pos, end, 12);
                    for (int i = 0; i < count * 3; i++)
                        geoset.Normals.Add(ReadF32(chunk, ref pos));
                }
                else if (tag is 0x50595450 or 0x544E4350) // PTYP / PCNT
                {
                    int count = ReadCount(chunk, ref pos, end, 4);
                    pos += count * 4;
                }
                else if (tag == 0x58545650) // PVTX
                {
                    int count = ReadCount(chunk, ref pos, end, 2);
                    for (int i = 0; i < count; i++)
                    {
                        geoset.Faces.Add(BinaryPrimitives.ReadUInt16LittleEndian(chunk[pos..]));
                        pos += 2;
                    }
                }
                else if (tag == 0x58444E47) // GNDX (byte per vertex)
                {
                    int count = ReadCount(chunk, ref pos, end, 1);
                    pos += count;
                }
                else if (tag == 0x4347544D) // MTGC (u32 per group)
                {
                    int count = ReadCount(chunk, ref pos, end, 4);
                    pos += count * 4;
                }
                else if (tag == 0x5354414D) // MATS — matrices, header, extents, then TANG/SKIN
                {
                    int count = ReadCount(chunk, ref pos, end, 4);
                    pos += count * 4;
                    if (pos + 4 <= end)
                        geoset.MaterialId = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
                    pos += 12; // material id, selection group, selection flags
                    if (model.Version > 800)
                    {
                        if (pos + 4 <= end)
                            geoset.Lod = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
                        pos += 4; // lod
                        if (pos + 80 <= end)
                            pos += 80; // lod name
                    }
                    pos += 28; // extent min/max/radius
                    if (pos + 4 <= end)
                    {
                        int seqExtents = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]), 0, 4096);
                        pos += 4;
                        for (int s = 0; s < seqExtents && pos + 28 <= end; s++)
                            pos += 28;
                    }
                    if (model.Version > 800)
                    {
                        if (pos + 4 <= end && BinaryPrimitives.ReadUInt32LittleEndian(chunk[pos..]) == 0x474E4154) // TANG
                        {
                            pos += 4;
                            int tangents = ReadCount(chunk, ref pos, end, 16);
                            pos += tangents * 16;
                        }
                        if (pos + 4 <= end && BinaryPrimitives.ReadUInt32LittleEndian(chunk[pos..]) == 0x4E494B53) // SKIN
                        {
                            pos += 4;
                            int skinBytes = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]), 0, end - pos - 4);
                            pos += 4 + skinBytes;
                        }
                    }
                }
                else if (tag == 0x53415655) // UVAS
                {
                    int setCount = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]), 0, 16);
                    pos += 4;
                    for (int set = 0; set < setCount && pos + 8 <= end; set++)
                    {
                        if (BinaryPrimitives.ReadUInt32LittleEndian(chunk[pos..]) != 0x53425655) // UVBS
                            break;
                        pos += 4;
                        int count = ReadCount(chunk, ref pos, end, 8);
                        for (int i = 0; i < count; i++)
                        {
                            float u = ReadF32(chunk, ref pos);
                            float v = ReadF32(chunk, ref pos);
                            if (set == 0)
                            {
                                geoset.Uvs.Add(u);
                                geoset.Uvs.Add(v);
                            }
                        }
                    }
                }
                else
                {
                    pos = end; // unknown tag — geometry of interest already captured
                }
            }
            pos = end;
            geosetIndex++;
            if (geoset.Positions.Count > 0 && geoset.Faces.Count > 0)
            {
                // Pad missing normals/uvs so the GL vertex stream stays aligned.
                int vertexCount = geoset.Positions.Count / 3;
                while (geoset.Normals.Count < vertexCount * 3)
                    geoset.Normals.Add(0f);
                while (geoset.Uvs.Count < vertexCount * 2)
                    geoset.Uvs.Add(0f);
                model.Geosets.Add(geoset);
            }
        }
    }

    private static string FixedCString(ReadOnlySpan<byte> bytes)
    {
        int len = bytes.IndexOf((byte)0);
        if (len < 0) len = bytes.Length;
        return Encoding.ASCII.GetString(bytes[..len]);
    }

    private static int ReadCount(ReadOnlySpan<byte> chunk, ref int pos, int end, int bytesPerItem)
    {
        int raw = BinaryPrimitives.ReadInt32LittleEndian(chunk[pos..]);
        pos += 4;
        if (raw < 0 || raw > MaxElements || (long)raw * bytesPerItem > end - pos)
            return 0;
        return raw;
    }

    private static float ReadF32(ReadOnlySpan<byte> chunk, ref int pos)
    {
        float value = BitConverter.ToSingle(chunk[pos..(pos + 4)]);
        pos += 4;
        return value;
    }
}
