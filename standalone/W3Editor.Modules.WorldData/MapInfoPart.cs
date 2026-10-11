using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.WorldData;

/// <summary>
/// Editable w3i identity header plus an opaque tail. HiveWE's complete MapInfo
/// structure is large and versioned; preserving the tail keeps players,
/// forces, tech tables and future fields byte-exact while basic metadata is
/// edited without pretending those sections were parsed.
/// </summary>
public sealed class MapInfoPart : IDocumentPart
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

    public string PartId => "mapinfo";
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => FormatVersion;
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
}

internal sealed class MapInfoCodec : IPartCodec
{
    public string PartId => "mapinfo";
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.w3i" };
    public int Order => 5;
    public bool CanHandle(MapFormatKind format) => format == MapFormatKind.War3Folder;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        byte[]? bytes = ctx.ReadFile("war3map.w3i");
        return bytes == null ? null : ReadBytes(bytes);
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not MapInfoPart info)
            throw new ArgumentException("Expected MapInfoPart.", nameof(part));
        ctx.Emit("war3map.w3i", WriteBytes(info));
    }

    internal static MapInfoPart ReadBytes(byte[] bytes)
    {
        W3MapInfoIdentity source = W3MapInfoIdentity.Parse(bytes);
        return new MapInfoPart
        {
            FormatVersion = source.FormatVersion,
            MapVersion = source.MapVersion,
            EditorVersion = source.EditorVersion,
            GameVersionMajor = source.GameVersionMajor,
            GameVersionMinor = source.GameVersionMinor,
            GameVersionPatch = source.GameVersionPatch,
            GameVersionBuild = source.GameVersionBuild,
            Name = source.Name,
            Author = source.Author,
            Description = source.Description,
            SuggestedPlayers = source.SuggestedPlayers,
            OpaqueTail = source.OpaqueTail,
        };
    }

    internal static byte[] WriteBytes(MapInfoPart info)
    {
        return new W3MapInfoIdentity
        {
            FormatVersion = info.FormatVersion,
            MapVersion = info.MapVersion,
            EditorVersion = info.EditorVersion,
            GameVersionMajor = info.GameVersionMajor,
            GameVersionMinor = info.GameVersionMinor,
            GameVersionPatch = info.GameVersionPatch,
            GameVersionBuild = info.GameVersionBuild,
            Name = info.Name,
            Author = info.Author,
            Description = info.Description,
            SuggestedPlayers = info.SuggestedPlayers,
            OpaqueTail = info.OpaqueTail,
        }.Write();
    }
}
