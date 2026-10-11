using System;
using System.Collections.Generic;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Formats;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.Terrain;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.Tests;

/// <summary>
/// 地面角点贴图交叉验证：HiveWE real_tile_texture 对照 C# GroundLayout。
/// 用法：godot --headless --path . res://tools/w3_ground_crosscheck.tscn -- --asset-free
/// </summary>
public partial class W3GroundCrosscheck : Node
{
    private const int CellSize = 64;

    private sealed class RompData
    {
        public required byte[] Mask;
        public required int TilesX;
        public required int TilesY;
    }

    public override void _Ready() => Run();

    private void Run()
    {
        bool assetFree = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--asset-free")
                assetFree = true;
        }

        string sourceRes = assetFree
            ? "res://testdata/hivewe_sample/war3map.w3e"
            : "res://war3/maps/LostTemple/source/war3map.w3e";
        string source = ProjectSettings.GlobalizePath(sourceRes);
        W3EMapBridge? level = W3EMapBridge.LoadFileStatic(source);
        if (level == null)
        {
            GD.PushError("W3_GROUND_CROSSCHECK_FAIL map missing");
            GetTree().Quit(1);
            return;
        }

        W3TextureProvider? provider = null;
        if (!assetFree)
        {
            provider = new W3TextureProvider();
            provider.LoadTerrainSlk("");
        }

        int layerCount = level.GroundTilesets.Length + 1;
        int[] cliffToGround = PrepareCliffToGround(level, provider, assetFree);
        byte[] tilesetExtended = PrepareTilesetExtended(level, provider, assetFree, layerCount);
        RompData romp = PrepareRompTiles(level);

        var bridge = new GroundLayoutCrosscheckBridge();
        bridge.Prepare(level, cliffToGround, tilesetExtended, !assetFree);

        byte[] hiveweRomp = HiveweRompMask(level);
        var rompMismatches = new List<string>();
        for (int y = 0; y < level.TileCountY(); y++)
        {
            for (int x = 0; x < level.TileCountX(); x++)
            {
                bool expectedRomp = hiveweRomp[y * level.TileCountX() + x] != 0;
                bool actualRomp = TileIsRomp(romp, x, y);
                if (expectedRomp != actualRomp && rompMismatches.Count < 24)
                    rompMismatches.Add($"{x},{y} actual={actualRomp} hive={expectedRomp}");
            }
        }

        GD.Print($"W3_GROUND_CROSSCHECK romp_mismatch_count={rompMismatches.Count}");
        foreach (string mismatch in rompMismatches)
            GD.Print("  ", mismatch);
        if (rompMismatches.Count > 0)
        {
            GetTree().Quit(4);
            return;
        }

        var mismatches = new List<string>();
        int mismatchCount = 0;
        var warsmashDisagreements = new List<string>();
        int warsmashDisagreementCount = 0;
        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                int expected = HiveweCornerTexture(level, cliffToGround, x, y);
                int warsmashExpected = WarsmashCornerTexture(level, cliffToGround, romp, x, y);
                if (warsmashExpected != expected)
                {
                    warsmashDisagreementCount++;
                    if (warsmashDisagreements.Count < 16)
                        warsmashDisagreements.Add($"{x},{y} hive={expected} warsmash={warsmashExpected}");
                }

                int actual = bridge.CornerTexture(x, y);
                if (actual == expected)
                    continue;
                mismatchCount++;
                if (mismatches.Count < 24)
                {
                    W3CornerData? c = level.GetCorner(x, y);
                    mismatches.Add(
                        $"{x},{y} actual={actual} hive={expected} ramp={c?.Ramp} ground={c?.GroundTexture} cliff={c?.CliffTexture}");
                }
            }
        }

        GD.Print($"W3_GROUND_CROSSCHECK mismatch_count={mismatchCount}");
        foreach (string mismatch in mismatches)
            GD.Print("  ", mismatch);
        if (mismatchCount != 0)
        {
            GetTree().Quit(2);
            return;
        }

        GD.Print($"W3_GROUND_CROSSCHECK hive_warsmash_disagreement_count={warsmashDisagreementCount}");
        foreach (string disagreement in warsmashDisagreements)
            GD.Print("  ", disagreement);

        List<string> maskMismatches = assetFree || provider == null
            ? new List<string>()
            : MaskOrientationMismatches(level, provider);
        GD.Print($"W3_GROUND_CROSSCHECK mask_mismatch_count={maskMismatches.Count}");
        for (int i = 0; i < Math.Min(24, maskMismatches.Count); i++)
            GD.Print("  ", maskMismatches[i]);
        if (maskMismatches.Count > 0)
        {
            GetTree().Quit(3);
            return;
        }

        GD.Print($"W3_GROUND_CROSSCHECK_OK corners={level.Width * level.Height}");
        GetTree().Quit(0);
    }

    private static int[] PrepareCliffToGround(W3EMapBridge level, W3TextureProvider? provider, bool assetFree)
    {
        if (assetFree || provider == null)
        {
            var zeros = new int[level.CliffTilesets.Length];
            return zeros;
        }

        var cliffToGround = new List<int>(level.CliffTilesets.Length);
        foreach (string cid in level.CliffTilesets)
        {
            string gid = provider.GetCliffGroundTile(cid);
            int mapped = 0;
            for (int gi = 0; gi < level.GroundTilesets.Length; gi++)
            {
                if (level.GroundTilesets[gi] == gid)
                {
                    mapped = gi;
                    break;
                }
            }

            cliffToGround.Add(mapped);
        }

        return cliffToGround.ToArray();
    }

    private static byte[] PrepareTilesetExtended(
        W3EMapBridge level, W3TextureProvider? provider, bool assetFree, int layerCount)
    {
        var extended = new byte[layerCount];
        if (assetFree || provider == null)
            return extended;

        for (int i = 0; i < level.GroundTilesets.Length; i++)
        {
            Image? img = provider.GetGroundImage(level.GroundTilesets[i]);
            if (img != null && img.GetWidth() >= img.GetHeight() * 2)
                extended[i] = 1;
        }

        Image? blightImg = provider.GetBlightImage(level.Tileset.ToString());
        if (blightImg != null && blightImg.GetWidth() >= blightImg.GetHeight() * 2)
            extended[level.GroundTilesets.Length] = 1;
        return extended;
    }

    private static RompData PrepareRompTiles(W3EMapBridge level)
    {
        int tilesX = level.TileCountX();
        int tilesY = level.TileCountY();
        var mask = new byte[tilesX * tilesY];
        for (int y = 0; y < tilesY; y++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                GDictionary transition = level.GetRampTransition(x, y);
                if (transition.Count == 0)
                    continue;
                MarkRompTile(mask, tilesX, tilesY, ReadVec2I(transition, "source", new Vector2I(x, y)));
                MarkRompTile(mask, tilesX, tilesY, ReadVec2I(transition, "hide_neighbor", new Vector2I(-1, -1)));
            }
        }

        return new RompData { Mask = mask, TilesX = tilesX, TilesY = tilesY };
    }

    private static Vector2I ReadVec2I(GDictionary dict, string key, Vector2I fallback)
    {
        if (!dict.ContainsKey(key))
            return fallback;
        return dict[key].AsVector2I();
    }

    private static void MarkRompTile(byte[] mask, int tilesX, int tilesY, Vector2I tile)
    {
        if (tile.X < 0 || tile.Y < 0 || tile.X >= tilesX || tile.Y >= tilesY)
            return;
        mask[tile.Y * tilesX + tile.X] = 1;
    }

    private static bool TileIsRomp(RompData romp, int x, int y)
    {
        if (x < 0 || y < 0 || x >= romp.TilesX || y >= romp.TilesY)
            return false;
        return romp.Mask[y * romp.TilesX + x] != 0;
    }

    private static int HiveweCornerTexture(W3EMapBridge level, int[] cliffToGround, int x, int y)
    {
        W3CornerData? c = level.GetCorner(x, y);
        if (c == null)
            return 0;
        bool nearCliff = false;
        bool nearRomp = false;
        for (int ty = y - 1; ty <= y; ty++)
        {
            for (int tx = x - 1; tx <= x; tx++)
            {
                if (tx < 0 || ty < 0 || tx >= level.TileCountX() || ty >= level.TileCountY())
                    continue;
                nearCliff = nearCliff || level.TileHasLayerCliff(tx, ty);
                nearRomp = nearRomp || level.RampTransitionCoversTile(tx, ty);
            }
        }

        if (nearRomp || (nearCliff && !c.Ramp))
        {
            int cliffTexture = c.CliffTexture;
            if (cliffTexture == 15)
                cliffTexture = 1;
            if (cliffTexture >= 0 && cliffTexture < cliffToGround.Length)
                return cliffToGround[cliffTexture];
        }

        if (c.Blight)
            return level.GroundTilesets.Length;
        return c.GroundTexture;
    }

    private static int WarsmashCornerTexture(
        W3EMapBridge level, int[] cliffToGround, RompData romp, int x, int y)
    {
        W3CornerData? current = level.GetCorner(x, y);
        if (current == null)
            return 0;
        bool suppressCliff = false;
        for (int ox = -1; ox <= 0; ox++)
        {
            for (int oy = -1; oy <= 0; oy++)
            {
                int tx = x + ox;
                int ty = y + oy;
                if (tx < 0 || ty < 0 || tx >= level.TileCountX() || ty >= level.TileCountY())
                    continue;
                bool isCliff = level.TileHasLayerCliff(tx, ty);
                if (isCliff && WarsmashFullRampWithoutRomp(level, romp, tx, ty))
                {
                    suppressCliff = true;
                    break;
                }

                if (TileIsRomp(romp, tx, ty) || isCliff)
                {
                    W3CornerData? owner = level.GetCorner(tx, ty);
                    int cliffTexture = owner?.CliffTexture ?? 0;
                    if (cliffTexture == 15)
                        cliffTexture = 1;
                    if (cliffTexture >= 0 && cliffTexture < cliffToGround.Length)
                        return cliffToGround[cliffTexture];
                }
            }

            if (suppressCliff)
                break;
        }

        if (current.Blight)
            return level.GroundTilesets.Length;
        return current.GroundTexture;
    }

    private static bool WarsmashFullRampWithoutRomp(W3EMapBridge level, RompData romp, int x, int y)
    {
        if (!level.IsRampEntrance(x, y))
            return false;
        for (int oy = 0; oy < 2; oy++)
        {
            for (int ox = 0; ox < 2; ox++)
            {
                if (TileIsRomp(romp, x + ox, y + oy))
                    return false;
            }
        }

        return true;
    }

    private static List<string> MaskOrientationMismatches(W3EMapBridge level, W3TextureProvider provider)
    {
        var mismatches = new List<string>();
        foreach (string tileId in level.GroundTilesets)
        {
            Image? image = provider.GetGroundImage(tileId);
            if (image == null)
            {
                mismatches.Add($"{tileId} image missing");
                continue;
            }

            bool extended = image.GetWidth() >= image.GetHeight() * 2;
            foreach (int sub in new[] { 1, 2, 4, 8 })
            {
                Image cell = SliceCell(image, sub, extended);
                int observed = 0;
                // Shader UV 为 (local.x, 1-local.y)；Image 坐标自上而下
                if (PatchAlpha(cell, 55, 55) > 0.9f) observed |= 1; // BR
                if (PatchAlpha(cell, 8, 55) > 0.9f) observed |= 2; // BL
                if (PatchAlpha(cell, 55, 8) > 0.9f) observed |= 4; // TR
                if (PatchAlpha(cell, 8, 8) > 0.9f) observed |= 8; // TL
                if (observed != sub)
                {
                    mismatches.Add(
                        $"{tileId} sub={sub} observed={observed} alpha=[{PatchAlpha(cell, 55, 55):F2},{PatchAlpha(cell, 8, 55):F2},{PatchAlpha(cell, 55, 8):F2},{PatchAlpha(cell, 8, 8):F2}]");
                }
            }
        }

        return mismatches;
    }

    private static Image SliceCell(Image img, int sub, bool extended)
    {
        var output = Image.CreateEmpty(CellSize, CellSize, false, Image.Format.Rgba8);
        output.Fill(new Color(0, 0, 0, 0));
        if (sub >= 16 && !extended)
            return output;
        int blockX = 0;
        int local = sub;
        if (sub >= 16)
        {
            blockX = 4;
            local = sub - 16;
        }

        int csz = img.GetHeight() / 4;
        if (csz <= 0)
            return output;
        int sx = (blockX + local % 4) * csz;
        int sy = local / 4 * csz;
        if (sx + csz > img.GetWidth() || sy + csz > img.GetHeight())
            return output;
        Image cell = img.GetRegion(new Rect2I(sx, sy, csz, csz));
        if (csz != CellSize)
            cell.Resize(CellSize, CellSize, Image.Interpolation.Bilinear);
        output.BlitRect(cell, new Rect2I(0, 0, CellSize, CellSize), Vector2I.Zero);
        return output;
    }

    private static float PatchAlpha(Image image, int centerX, int centerY)
    {
        float total = 0f;
        int count = 0;
        for (int y = centerY - 3; y <= centerY + 3; y++)
        {
            for (int x = centerX - 3; x <= centerX + 3; x++)
            {
                total += image.GetPixel(x, y).A;
                count++;
            }
        }

        return total / count;
    }

    private static byte[] HiveweRompMask(W3EMapBridge level)
    {
        int tilesX = level.TileCountX();
        int tilesY = level.TileCountY();
        var mask = new byte[tilesX * tilesY];
        for (int i = 0; i < tilesX; i++)
        {
            for (int j = 0; j < tilesY; j++)
            {
                if (j < tilesY - 1 && HiveweVerticalRamp(level, i, j))
                {
                    mask[j * tilesX + i] = 1;
                    mask[(j + 1) * tilesX + i] = 1;
                    continue;
                }

                if (i < tilesX - 1 && HiveweHorizontalRamp(level, i, j))
                {
                    mask[j * tilesX + i] = 1;
                    mask[j * tilesX + i + 1] = 1;
                }
            }
        }

        return mask;
    }

    private static bool HiveweVerticalRamp(W3EMapBridge level, int x, int y)
    {
        W3CornerData? bl = level.GetCorner(x, y);
        W3CornerData? br = level.GetCorner(x + 1, y);
        W3CornerData? tl = level.GetCorner(x, y + 1);
        W3CornerData? tr = level.GetCorner(x + 1, y + 1);
        W3CornerData? ttl = level.GetCorner(x, y + 2);
        W3CornerData? ttr = level.GetCorner(x + 1, y + 2);
        if (bl == null || br == null || tl == null || tr == null || ttl == null || ttr == null)
            return false;
        int ae = Math.Min(bl.LayerHeight, ttl.LayerHeight);
        int cf = Math.Min(br.LayerHeight, ttr.LayerHeight);
        if (tl.LayerHeight != ae || tr.LayerHeight != cf)
            return false;
        if (!(bl.Ramp == tl.Ramp && bl.Ramp == ttl.Ramp))
            return false;
        if (!(br.Ramp == tr.Ramp && br.Ramp == ttr.Ramp))
            return false;
        if (bl.Ramp == br.Ramp)
            return false;
        return CliffTransExists(new[] { ttl, ttr, br, bl }, Math.Min(ae, cf));
    }

    private static bool HiveweHorizontalRamp(W3EMapBridge level, int x, int y)
    {
        W3CornerData? bl = level.GetCorner(x, y);
        W3CornerData? br = level.GetCorner(x + 1, y);
        W3CornerData? tl = level.GetCorner(x, y + 1);
        W3CornerData? tr = level.GetCorner(x + 1, y + 1);
        W3CornerData? brr = level.GetCorner(x + 2, y);
        W3CornerData? trr = level.GetCorner(x + 2, y + 1);
        if (bl == null || br == null || tl == null || tr == null || brr == null || trr == null)
            return false;
        int ae = Math.Min(bl.LayerHeight, brr.LayerHeight);
        int bf = Math.Min(tl.LayerHeight, trr.LayerHeight);
        if (br.LayerHeight != ae || tr.LayerHeight != bf)
            return false;
        if (!(bl.Ramp == br.Ramp && bl.Ramp == brr.Ramp))
            return false;
        if (!(tl.Ramp == tr.Ramp && tl.Ramp == trr.Ramp))
            return false;
        if (bl.Ramp == tl.Ramp)
            return false;
        return CliffTransExists(new[] { tl, trr, brr, bl }, Math.Min(ae, bf));
    }

    private static bool CliffTransExists(W3CornerData[] corners, int baseHeight)
    {
        var tag = new System.Text.StringBuilder(4);
        foreach (W3CornerData corner in corners)
        {
            int diff = corner.LayerHeight - baseHeight;
            int code = corner.Ramp ? 76 : 65; // L / A
            code += diff * (corner.Ramp ? -4 : 1);
            tag.Append((char)code);
        }

        string rel = $"Doodads/Terrain/CliffTrans/CliffTrans{tag}0.mdx";
        return !string.IsNullOrEmpty(new W3PathResolverBridge().ResolvePath(rel));
    }
}
