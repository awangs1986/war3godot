using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.Terrain;

/// <summary>
/// war3map.w3e reader/writer. Hands bytes back to the kernel rather than
/// writing files, so terrain participates in the single generational commit
/// with the placement files (external review 4.2 / 4.3).
/// </summary>
public sealed class TerrainCodec : IPartCodec
{
    public const string W3eFileName = "war3map.w3e";

    public string PartId => TerrainPart.Id;
    public IReadOnlyList<string> FileNames { get; } = new[] { W3eFileName };
    public int Order => 0; // terrain first: placements snap to its heights

    public bool CanHandle(MapFormatKind format) =>
        format is MapFormatKind.War3Folder or MapFormatKind.War3EnvironmentFile;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        byte[]? bytes = ctx.ReadFile(W3eFileName);
        if (bytes == null)
        {
            ctx.Log.Warn($"{W3eFileName} not found in {ctx.Folder}; terrain part not loaded.");
            return null;
        }
        return new TerrainPart(W3EMap.LoadBytes(bytes));
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx) =>
        ctx.Emit(W3eFileName, ((TerrainPart)part).Map.SaveBytes());
}
