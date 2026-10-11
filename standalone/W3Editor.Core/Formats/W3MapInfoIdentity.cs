using System.Text;

namespace W3Editor.Formats;

/// <summary>
/// Editable identity prefix of war3map.w3i. Everything after the four strings
/// is retained byte-for-byte because the remaining tables are versioned and
/// independently owned. This is the same boundary used by part.mapinfo.
/// </summary>
public sealed class W3MapInfoIdentity
{
    public int FormatVersion;
    public int MapVersion;
    public int EditorVersion;
    public int GameVersionMajor;
    public int GameVersionMinor;
    public int GameVersionPatch;
    public int GameVersionBuild;
    public string Name = "";
    public string Author = "";
    public string Description = "";
    public string SuggestedPlayers = "";
    public byte[] OpaqueTail = Array.Empty<byte>();

    public static W3MapInfoIdentity Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Require(stream, 4);
        var result = new W3MapInfoIdentity { FormatVersion = reader.ReadInt32() };
        if (result.FormatVersion >= 18)
        {
            Require(stream, 8);
            result.MapVersion = reader.ReadInt32();
            result.EditorVersion = reader.ReadInt32();
        }
        if (result.FormatVersion >= 28)
        {
            Require(stream, 16);
            result.GameVersionMajor = reader.ReadInt32();
            result.GameVersionMinor = reader.ReadInt32();
            result.GameVersionPatch = reader.ReadInt32();
            result.GameVersionBuild = reader.ReadInt32();
        }
        result.Name = ReadCString(reader, stream);
        result.Author = ReadCString(reader, stream);
        result.Description = ReadCString(reader, stream);
        result.SuggestedPlayers = ReadCString(reader, stream);
        result.OpaqueTail = reader.ReadBytes(checked((int)(stream.Length - stream.Position)));
        return result;
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        if (FormatVersion >= 18)
        {
            writer.Write(MapVersion);
            writer.Write(EditorVersion);
        }
        if (FormatVersion >= 28)
        {
            writer.Write(GameVersionMajor);
            writer.Write(GameVersionMinor);
            writer.Write(GameVersionPatch);
            writer.Write(GameVersionBuild);
        }
        WriteCString(writer, Name);
        WriteCString(writer, Author);
        WriteCString(writer, Description);
        WriteCString(writer, SuggestedPlayers);
        writer.Write(OpaqueTail);
        return stream.ToArray();
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
                throw new InvalidDataException("w3i string is too long");
        }
        throw new InvalidDataException("w3i has an unterminated string");
    }

    private static void WriteCString(BinaryWriter writer, string? value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value ?? ""));
        writer.Write((byte)0);
    }

    private static void Require(Stream stream, int bytes)
    {
        if (stream.Length - stream.Position < bytes)
            throw new InvalidDataException("w3i header is truncated");
    }
}
