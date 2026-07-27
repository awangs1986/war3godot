using System.Text;

namespace W3Editor.Formats;

/// <summary>One war3map.w3r rectangle. Coordinates are Warcraft world X/Y.</summary>
public class W3RegionRecord
{
    public float Left;
    public float Bottom;
    public float Right;
    public float Top;
    public string Name = "";
    public int CreationNumber;
    public string WeatherId = "\0\0\0\0";
    public string AmbientId = "";
    public byte R = 255;
    public byte G;
    public byte B;
    public byte A = 255;

    public virtual W3RegionRecord Clone() => (W3RegionRecord)MemberwiseClone();
}

/// <summary>
/// Byte-faithful war3map.w3r v5 reader/writer. HiveWE reads the on-disk color
/// as BGRA and exposes it as RGBA; this class keeps that same boundary.
/// </summary>
public sealed class W3RegionsFile
{
    public int Version { get; set; } = 5;
    public List<W3RegionRecord> Regions { get; } = new();

    public static W3RegionsFile LoadBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Require(stream, 8);
        var result = new W3RegionsFile { Version = reader.ReadInt32() };
        int count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000)
            throw new InvalidDataException($"invalid w3r region count {count}");
        for (int i = 0; i < count; i++)
        {
            Require(stream, 16);
            var region = new W3RegionRecord
            {
                Left = reader.ReadSingle(),
                Bottom = reader.ReadSingle(),
                Right = reader.ReadSingle(),
                Top = reader.ReadSingle(),
                Name = ReadCString(reader, stream),
            };
            Require(stream, 8);
            region.CreationNumber = reader.ReadInt32();
            region.WeatherId = Encoding.Latin1.GetString(reader.ReadBytes(4));
            region.AmbientId = ReadCString(reader, stream);
            Require(stream, 4);
            region.B = reader.ReadByte();
            region.G = reader.ReadByte();
            region.R = reader.ReadByte();
            region.A = reader.ReadByte();
            result.Regions.Add(region);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("w3r has trailing bytes");
        return result;
    }

    public byte[] SaveBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(Regions.Count);
        foreach (W3RegionRecord region in Regions)
        {
            writer.Write(region.Left);
            writer.Write(region.Bottom);
            writer.Write(region.Right);
            writer.Write(region.Top);
            WriteCString(writer, region.Name);
            writer.Write(region.CreationNumber);
            byte[] weather = Encoding.Latin1.GetBytes(region.WeatherId ?? "");
            writer.Write(weather, 0, Math.Min(4, weather.Length));
            for (int i = weather.Length; i < 4; i++)
                writer.Write((byte)0);
            WriteCString(writer, region.AmbientId);
            writer.Write(region.B);
            writer.Write(region.G);
            writer.Write(region.R);
            writer.Write(region.A);
        }
        return stream.ToArray();
    }

    public int NextCreationNumber() => Regions.Count == 0
        ? 0
        : Regions.Max(region => region.CreationNumber) + 1;

    public string NextName()
    {
        for (int i = 0; ; i++)
        {
            string candidate = $"Region {i:000}";
            if (Regions.All(region => region.Name != candidate))
                return candidate;
        }
    }

    public W3RegionRecord[] CloneRegions() => Regions.Select(region => region.Clone()).ToArray();

    private static void Require(Stream stream, int count)
    {
        if (stream.Length - stream.Position < count)
            throw new InvalidDataException("w3r is truncated");
    }

    private static string ReadCString(BinaryReader reader, Stream stream)
    {
        var bytes = new List<byte>();
        while (stream.Position < stream.Length)
        {
            byte value = reader.ReadByte();
            if (value == 0)
                return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(value);
            if (bytes.Count > 1_048_576)
                throw new InvalidDataException("w3r string is too long");
        }
        throw new InvalidDataException("w3r has an unterminated string");
    }

    private static void WriteCString(BinaryWriter writer, string? value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value ?? ""));
        writer.Write((byte)0);
    }
}
