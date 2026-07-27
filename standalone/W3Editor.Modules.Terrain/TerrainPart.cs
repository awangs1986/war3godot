using System.Buffers.Binary;
using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.Terrain;

/// <summary>
/// The terrain corner grid as a document part. Wraps the existing
/// <see cref="W3EMap"/> model rather than reimplementing it — external review
/// 4.10 was about the same algorithm existing three times; this must not add a
/// fourth.
/// </summary>
public sealed class TerrainPart : IDocumentPart
{
    public const string Id = "terrain";
    public const int CornerBytes = 16;

    public TerrainPart(W3EMap map) => Map = map;

    public W3EMap Map { get; }
    public string PartId => Id;
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Grid dimensions plus palette sizes: exactly the things that invalidate a
    /// corner-index delta. A resize or a tileset swap changes this, so stale
    /// undo entries are rejected instead of writing to relocated corners.
    /// </summary>
    public long ShapeFingerprint
    {
        get
        {
            long h = 1469598103934665603L;
            void Mix(long v)
            {
                h ^= v;
                h *= 1099511628211L;
            }
            Mix(Map.Width);
            Mix(Map.Height);
            Mix(Map.GroundTilesets.Count);
            Mix(Map.CliffTilesets.Count);
            return h;
        }
    }

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    // -----------------------------------------------------------------
    // Undo support
    // -----------------------------------------------------------------

    /// <summary>Snapshot of every corner, for diffing at stroke end.</summary>
    public byte[] PackAll()
    {
        var data = new byte[Map.Corners.Length * CornerBytes];
        for (int i = 0; i < Map.Corners.Length; i++)
            Pack(Map.Corners[i], data.AsSpan(i * CornerBytes));
        return data;
    }

    /// <summary>
    /// Builds the delta that restores <paramref name="before"/>, or null when
    /// the stroke changed nothing (HiveWE discards no-op strokes too).
    /// </summary>
    public TerrainDelta? DiffFrom(byte[] before)
    {
        byte[] after = PackAll();
        if (before.Length != after.Length)
            return null; // shape changed mid-stroke; fingerprint check will catch it
        var indices = new List<int>();
        for (int i = 0; i < after.Length / CornerBytes; i++)
        {
            if (!before.AsSpan(i * CornerBytes, CornerBytes)
                    .SequenceEqual(after.AsSpan(i * CornerBytes, CornerBytes)))
                indices.Add(i);
        }
        if (indices.Count == 0)
            return null;

        var payload = new byte[indices.Count * CornerBytes];
        for (int i = 0; i < indices.Count; i++)
            before.AsSpan(indices[i] * CornerBytes, CornerBytes).CopyTo(payload.AsSpan(i * CornerBytes));
        return new TerrainDelta(ShapeFingerprint, indices.ToArray(), payload);
    }

    internal void ApplyPacked(int[] indices, byte[] packed)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= 0 && indices[i] < Map.Corners.Length)
                Unpack(packed.AsSpan(i * CornerBytes), Map.Corners[indices[i]]);
        }
    }

    internal byte[] CapturePacked(int[] indices)
    {
        var payload = new byte[indices.Length * CornerBytes];
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= 0 && indices[i] < Map.Corners.Length)
                Pack(Map.Corners[indices[i]], payload.AsSpan(i * CornerBytes));
        }
        return payload;
    }

    /// <summary>Lossless encoding — War3 quantization belongs only at the file boundary.</summary>
    private static void Pack(W3Corner c, Span<byte> output)
    {
        BinaryPrimitives.WriteSingleLittleEndian(output, c.GroundHeight);
        BinaryPrimitives.WriteSingleLittleEndian(output[4..], c.WaterHeight);
        ushort flags = 0;
        if (c.MapEdge) flags |= 1;
        if (c.Ramp) flags |= 2;
        if (c.Blight) flags |= 4;
        if (c.Water) flags |= 8;
        if (c.Boundary) flags |= 16;
        BinaryPrimitives.WriteUInt16LittleEndian(output[8..], flags);
        output[10] = (byte)c.GroundTexture;
        output[11] = (byte)c.GroundVariation;
        output[12] = (byte)c.CliffTexture;
        output[13] = (byte)c.CliffVariation;
        output[14] = (byte)c.LayerHeight;
        output[15] = 0;
    }

    private static void Unpack(ReadOnlySpan<byte> input, W3Corner c)
    {
        c.GroundHeight = BinaryPrimitives.ReadSingleLittleEndian(input);
        c.WaterHeight = BinaryPrimitives.ReadSingleLittleEndian(input[4..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(input[8..]);
        c.MapEdge = (flags & 1) != 0;
        c.Ramp = (flags & 2) != 0;
        c.Blight = (flags & 4) != 0;
        c.Water = (flags & 8) != 0;
        c.Boundary = (flags & 16) != 0;
        c.GroundTexture = input[10];
        c.GroundVariation = input[11];
        c.CliffTexture = input[12];
        c.CliffVariation = input[13];
        c.LayerHeight = input[14];
    }
}

/// <summary>Per-corner terrain delta.</summary>
public sealed class TerrainDelta : IUndoDelta
{
    private readonly int[] _indices;
    private readonly byte[] _packed;

    public TerrainDelta(long shapeFingerprint, int[] indices, byte[] packed)
    {
        ShapeFingerprint = shapeFingerprint;
        _indices = indices;
        _packed = packed;
    }

    public long ShapeFingerprint { get; }
    public int ApproximateBytes => _packed.Length + _indices.Length * sizeof(int) + 48;

    public IUndoDelta Apply(IDocumentPart part)
    {
        var terrain = (TerrainPart)part;
        byte[] inverse = terrain.CapturePacked(_indices);
        terrain.ApplyPacked(_indices, _packed);
        return new TerrainDelta(ShapeFingerprint, _indices, inverse);
    }
}
