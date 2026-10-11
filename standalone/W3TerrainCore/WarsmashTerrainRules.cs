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
    private byte[]? _authoredCliffTextures;
    private readonly List<CliffPlacement> _missingModels = new();
    private readonly HashSet<int> _missingTiles = new();
    public IReadOnlyList<CliffPlacement> MissingModels => _missingModels.AsReadOnly();

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
        _cliffVariation = cliffVariation ?? ResolveVariation;
    }

    /// <summary>
    /// The authored per-corner cliff texture as of the latest RebuildCliffs()
    /// call. Ramp texture propagation lives in a separate render-only array.
    /// HiveWE's sealed real_tile_texture reads the AUTHORED value
    /// — GroundLayout must use this, not the
    /// effective ramp texture.
    /// </summary>
    public byte AuthoredCliffTexture(int x, int y)
    {
        int index = y * _map.Width + x;
        return _authoredCliffTextures != null
            && index >= 0 && index < _authoredCliffTextures.Length
            ? _authoredCliffTextures[index]
            : _map.GetCorner(x, y).CliffTexture;
    }

    public IReadOnlyList<CliffPlacement> RebuildCliffs()
    {
        _map.InitializeRenderCorners();
        _missingModels.Clear();
        _missingTiles.Clear();
        // The editor refreshes only a dirty region of a persistent mirror.
        // Never stamp derived ramp textures into its authored corner fields:
        // a later rebuild would mistake those values for authored data.
        _authoredCliffTextures = new byte[_map.Width * _map.Height];
        for (int i = 0; i < _map.Corners.Length && i < _authoredCliffTextures.Length; i++)
            _authoredCliffTextures[i] = _map.Corners[i].CliffTexture;
        var renderCliffTextures = (byte[])_authoredCliffTextures.Clone();
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
                int cliffTexture = ResolveCliffTexture(renderCliffTextures, x, y);

                bool insideFacingEdge = !(facingDown && y == 0) && !(!facingDown && y >= _map.Height - 2) &&
                    !(facingLeft && x == 0) && !(!facingLeft && x >= _map.Width - 2);
                if (insideFacingEdge)
                {
                    bool verticalRamp = bl.EffectiveRamp != br.EffectiveRamp && tl.EffectiveRamp != tr.EffectiveRamp;
                    bool horizontalRamp = bl.EffectiveRamp != tl.EffectiveRamp && br.EffectiveRamp != tr.EffectiveRamp;
                    if (verticalRamp || horizontalRamp)
                    {
                        // Warsmash checks the two facing neighbours independently.
                        // When both ramp patterns match, a diagonal check is wrong.
                        bool blocked = (verticalRamp && _map.GetCorner(x, y + (facingDown ? -1 : 1)).Cliff)
                            || (horizontalRamp && _map.GetCorner(x + (facingLeft ? -1 : 1), y).Cliff);
                        int tlHeight = tl.LayerHeight - baseLayer;
                        int trHeight = tr.LayerHeight - baseLayer;
                        int brHeight = br.LayerHeight - baseLayer;
                        int blHeight = bl.LayerHeight - baseLayer;
                        bool invalid = blocked;
                        if (!blocked)
                        {
                            // Match DISALLOW_HEIGHT_3_RAMPS exactly: only the first
                            // excessive corner (TL, TR, BR, BL) is cleared per visit.
                            // Keep this derived state out of W3E and HiveWE ownership.
                            W3TerrainMap.Corner? excessive = tlHeight > 1 ? tl : trHeight > 1 ? tr
                                : brHeight > 1 ? br : blHeight > 1 ? bl : null;
                            if (excessive != null)
                            {
                                excessive.RampSuppressed = true;
                                invalid = true;
                            }
                        }
                        if (!invalid)
                        {
                            string tag = string.Concat(
                                RampLetter(tlHeight, tl.EffectiveRamp), RampLetter(trHeight, tr.EffectiveRamp),
                                RampLetter(brHeight, br.EffectiveRamp), RampLetter(blHeight, bl.EffectiveRamp));
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
                                // Preserve Warsmash's within-pass propagation
                                // for cliff selection, without changing HiveWE's
                                // authored ground-texture ownership.
                                renderCliffTextures[y * _map.Width + x] = (byte)cliffTexture;
                                renderCliffTextures[y * _map.Width + x + 1] = (byte)cliffTexture;
                                renderCliffTextures[(y + 1) * _map.Width + x] = (byte)cliffTexture;
                                renderCliffTextures[(y + 1) * _map.Width + x + 1] = (byte)cliffTexture;
                                int neighbourX = x + (horizontalRamp ? (facingLeft ? -1 : 1) : 0);
                                int neighbourY = y + (verticalRamp ? (facingDown ? -1 : 1) : 0);
                                // A two-tile ramp can replace an earlier missing ordinary cliff.
                                _missingTiles.Remove(y * _map.TileWidth + x);
                                _missingTiles.Remove(neighbourY * _map.TileWidth + neighbourX);
                                _missingModels.RemoveAll(p => (p.X == x && p.Y == y)
                                    || (p.X == neighbourX && p.Y == neighbourY));
                                W3TerrainMap.Corner neighbour = _map.GetCorner(neighbourX, neighbourY);
                                renderCliffTextures[neighbourY * _map.Width + neighbourX] = (byte)cliffTexture;
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
                var placement = new CliffPlacement(x, y, cliffTag, false, cliffTexture, variation, baseLayer, cliffPath);
                if (_modelExists(cliffPath))
                    cliffs.Add(placement);
                else
                {
                    _missingModels.Add(placement);
                    _missingTiles.Add(y * _map.TileWidth + x);
                }
            }
        }
        return cliffs;
    }

    public bool GroundExists(int x, int y)
    {
        if (!_map.ContainsTile(x, y))
            return false;
        // A missing ordinary cliff must not remove the only remaining surface.
        // Keep the heightfield fallback and report the missing asset separately.
        if (_missingTiles.Contains(y * _map.TileWidth + x))
            return true;
        W3TerrainMap.Corner corner = _map.GetCorner(x, y);
        return !((corner.Cliff || corner.Romp) && !_map.IsRampEntrance(x, y));
    }

    private int PlacementBase(int x, int y) => Min4(
        _map.GetCorner(x, y).LayerHeight,
        _map.GetCorner(x + 1, y).LayerHeight,
        _map.GetCorner(x, y + 1).LayerHeight,
        _map.GetCorner(x + 1, y + 1).LayerHeight);

    private int ResolveCliffTexture(byte[] textures, int x, int y)
    {
        int bl = y * _map.Width + x;
        int texture = textures[bl];
        if (texture != 15)
            return texture;
        if (textures[bl + _map.Width] != 15) return textures[bl + _map.Width];
        if (textures[bl + _map.Width + 1] != 15) return textures[bl + _map.Width + 1];
        if (textures[bl + 1] != 15) return textures[bl + 1];
        return 1;
    }

    private static char RampLetter(int relativeHeight, bool ramp) =>
        ramp ? (char)('L' - (relativeHeight * 4)) : (char)('A' + relativeHeight);

    private static int Min4(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    /// <summary>HiveWE variation policy shared by all production callers.</summary>
    public static int ResolveVariation(string tag, int authored) =>
        Math.Clamp(authored, 0, CliffVariationMaximum.GetValueOrDefault(tag, 0));

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
