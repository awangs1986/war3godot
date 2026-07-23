namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Direct C# port of Warsmash Terrain.updateCliffMeshes and ground-hole rules.
/// Warsmash is authoritative when its behavior differs from HiveWE.
/// </summary>
public sealed class WarsmashTerrainRules
{
    public sealed record CliffPlacement(
        int X,
        int Y,
        string Tag,
        bool IsRamp,
        int CliffTexture,
        int Variation,
        int BaseLayer,
        string ModelPath,
        int? SourceX = null,
        int? SourceY = null);

    private readonly W3TerrainMap _map;
    private readonly Func<string, bool> _modelExists;
    private readonly Func<int, string> _cliffModelDirectory;
    private readonly Func<int, string> _rampModelDirectory;
    private readonly Func<string, int, int> _cliffVariation;

    public WarsmashTerrainRules(
        W3TerrainMap map,
        Func<string, bool>? modelExists = null,
        Func<int, string>? cliffModelDirectory = null,
        Func<int, string>? rampModelDirectory = null,
        Func<string, int, int>? cliffVariation = null)
    {
        _map = map;
        _modelExists = modelExists ?? (_ => true);
        _cliffModelDirectory = cliffModelDirectory ?? (_ => "Cliffs");
        _rampModelDirectory = rampModelDirectory ?? (_ => "CliffTrans");
        _cliffVariation = cliffVariation ?? ((_, authored) => Math.Max(0, authored));
    }

    public IReadOnlyList<CliffPlacement> RebuildCliffs()
    {
        _map.InitializeRenderCorners();
        var cliffs = new List<CliffPlacement>();
        for (int x = 0; x < _map.TileWidth; x++)
        {
            for (int y = 0; y < _map.TileHeight; y++)
            {
                W3TerrainMap.Corner bl = _map.GetCorner(x, y);
                if (!bl.Cliff || bl.HideCliff)
                    continue;
                W3TerrainMap.Corner br = _map.GetCorner(x + 1, y);
                W3TerrainMap.Corner tl = _map.GetCorner(x, y + 1);
                W3TerrainMap.Corner tr = _map.GetCorner(x + 1, y + 1);
                int baseLayer = Min4(bl.LayerHeight, br.LayerHeight, tl.LayerHeight, tr.LayerHeight);
                bool facingDown = tl.LayerHeight >= bl.LayerHeight && tr.LayerHeight >= br.LayerHeight;
                bool facingLeft = br.LayerHeight >= bl.LayerHeight && tr.LayerHeight >= tl.LayerHeight;
                int cliffTexture = ResolveCliffTexture(bl, br, tl, tr);

                bool insideFacingEdge = !(facingDown && y == 0) && !(!facingDown && y >= _map.Height - 2) &&
                    !(facingLeft && x == 0) && !(!facingLeft && x >= _map.Width - 2);
                if (insideFacingEdge)
                {
                    bool verticalRamp = bl.Ramp != br.Ramp && tl.Ramp != tr.Ramp;
                    bool horizontalRamp = bl.Ramp != tl.Ramp && br.Ramp != tr.Ramp;
                    if (verticalRamp || horizontalRamp)
                    {
                        int blockedX = x + (horizontalRamp ? (facingLeft ? -1 : 1) : 0);
                        int blockedY = y + (verticalRamp ? (facingDown ? -1 : 1) : 0);
                        bool blocked = _map.ContainsTile(blockedX, blockedY) && _map.GetCorner(blockedX, blockedY).Cliff;
                        int tlHeight = tl.LayerHeight - baseLayer;
                        int trHeight = tr.LayerHeight - baseLayer;
                        int brHeight = br.LayerHeight - baseLayer;
                        int blHeight = bl.LayerHeight - baseLayer;
                        bool invalid = blocked || tlHeight > 1 || trHeight > 1 || brHeight > 1 || blHeight > 1;
                        if (!invalid)
                        {
                            string tag = string.Concat(
                                RampLetter(tlHeight, tl.Ramp), RampLetter(trHeight, tr.Ramp),
                                RampLetter(brHeight, br.Ramp), RampLetter(blHeight, bl.Ramp));
                            string directory = _rampModelDirectory(cliffTexture);
                            string modelPath = $"Doodads/Terrain/{directory}/{directory}{tag}0.mdx";
                            if (_modelExists(modelPath))
                            {
                                int placementX = x + (horizontalRamp && facingLeft ? -1 : 0);
                                int placementY = y - (verticalRamp && facingDown ? 1 : 0);
                                cliffs.RemoveAll(item => item.X == placementX && item.Y == placementY);
                                cliffs.Add(new CliffPlacement(placementX, placementY, tag, true, cliffTexture, 0,
                                    PlacementBase(placementX, placementY), modelPath, x, y));
                                bl.Romp = true;
                                bl.CliffTexture = (byte)cliffTexture;
                                br.CliffTexture = (byte)cliffTexture;
                                tl.CliffTexture = (byte)cliffTexture;
                                tr.CliffTexture = (byte)cliffTexture;
                                W3TerrainMap.Corner neighbour = _map.GetCorner(
                                    x + (horizontalRamp ? (facingLeft ? -1 : 1) : 0),
                                    y + (verticalRamp ? (facingDown ? -1 : 1) : 0));
                                neighbour.CliffTexture = (byte)cliffTexture;
                                neighbour.Romp = true;
                                continue;
                            }
                        }
                    }
                }

                if (_map.IsRampEntrance(x, y))
                    continue;
                string cliffTag = string.Concat(
                    (char)('A' + tl.LayerHeight - baseLayer),
                    (char)('A' + tr.LayerHeight - baseLayer),
                    (char)('A' + br.LayerHeight - baseLayer),
                    (char)('A' + bl.LayerHeight - baseLayer));
                if (cliffTag == "AAAA")
                    continue;
                int variation = _cliffVariation(cliffTag, bl.CliffVariation);
                string cliffDirectory = _cliffModelDirectory(cliffTexture);
                string cliffPath = $"Doodads/Terrain/{cliffDirectory}/{cliffDirectory}{cliffTag}{variation}.mdx";
                cliffs.Add(new CliffPlacement(x, y, cliffTag, false, cliffTexture, variation, baseLayer, cliffPath));
            }
        }
        return cliffs;
    }

    public bool GroundExists(int x, int y)
    {
        if (!_map.ContainsTile(x, y))
            return false;
        W3TerrainMap.Corner corner = _map.GetCorner(x, y);
        return !((corner.Cliff || corner.Romp) && !_map.IsRampEntrance(x, y));
    }

    private int PlacementBase(int x, int y) => Min4(
        _map.GetCorner(x, y).LayerHeight,
        _map.GetCorner(x + 1, y).LayerHeight,
        _map.GetCorner(x, y + 1).LayerHeight,
        _map.GetCorner(x + 1, y + 1).LayerHeight);

    private static int ResolveCliffTexture(W3TerrainMap.Corner bl, W3TerrainMap.Corner br, W3TerrainMap.Corner tl, W3TerrainMap.Corner tr)
    {
        int texture = bl.CliffTexture;
        if (texture != 15)
            return texture;
        if (tl.CliffTexture != 15) return tl.CliffTexture;
        if (tr.CliffTexture != 15) return tr.CliffTexture;
        if (br.CliffTexture != 15) return br.CliffTexture;
        return 1;
    }

    private static char RampLetter(int relativeHeight, bool ramp) =>
        ramp ? (char)('L' - (relativeHeight * 4)) : (char)('A' + relativeHeight);

    private static int Min4(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    /// <summary>
    /// HiveWE cliff variation table — highest authored variation index per tag.
    /// Shared so both renderers clamp identically.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> CliffVariationMaximum = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AAAB"]=1,["AAAC"]=1,["AABA"]=1,["AABB"]=2,["AABC"]=0,["AACA"]=1,["AACB"]=0,["AACC"]=1,
        ["ABAA"]=1,["ABAB"]=1,["ABAC"]=0,["ABBA"]=2,["ABBB"]=1,["ABBC"]=0,["ABCA"]=0,["ABCB"]=0,["ABCC"]=0,
        ["ACAA"]=1,["ACAB"]=0,["ACAC"]=1,["ACBA"]=0,["ACBB"]=0,["ACBC"]=0,["ACCA"]=1,["ACCB"]=0,["ACCC"]=1,
        ["BAAA"]=1,["BAAB"]=1,["BAAC"]=0,["BABA"]=1,["BABB"]=1,["BABC"]=0,["BACA"]=0,["BACB"]=0,["BACC"]=0,
        ["BBAA"]=1,["BBAB"]=1,["BBAC"]=0,["BBBA"]=1,["BBCA"]=0,["BCAA"]=0,["BCAB"]=0,["BCAC"]=0,["BCBA"]=0,["BCCA"]=0,
        ["CAAA"]=1,["CAAB"]=0,["CAAC"]=1,["CABA"]=0,["CABB"]=0,["CABC"]=0,["CACA"]=1,["CACB"]=0,["CACC"]=1,
        ["CBAA"]=0,["CBAB"]=0,["CBAC"]=0,["CBBA"]=0,["CBCA"]=0,["CCAA"]=1,["CCAB"]=0,["CCAC"]=1,["CCBA"]=0,["CCCA"]=1,
    };
}
