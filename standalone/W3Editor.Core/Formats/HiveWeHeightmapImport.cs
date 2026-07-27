namespace W3Editor.Formats;

/// <summary>Exact HiveWE heightmap import rule from HiveWE::import_heightmap.</summary>
public static class HiveWeHeightmapImport
{
    /// <param name="readRedTopDown">Reads an image red channel with y=0 at the image top.</param>
    public static void Apply(
        W3EMap map,
        int imageWidth,
        int imageHeight,
        Func<int, int, byte> readRedTopDown)
    {
        if (imageWidth != map.Width || imageHeight != map.Height)
            throw new InvalidDataException(
                $"Image size {imageWidth}x{imageHeight} does not match terrain corners {map.Width}x{map.Height}.");

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            byte red = readRedTopDown(x, map.Height - 1 - y);
            map.Corners[y * map.Width + x].GroundHeight = (red - 128f) / 8f;
        }
    }
}
