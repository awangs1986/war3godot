using System;
using System.Collections.Generic;
using Diablo1Like.MapRuntime;
using Godot;
using W3Compat.Formats;
using GArray = Godot.Collections.Array;

namespace Diablo1Like.W3.Formats;

/// <summary>
/// war3map.doo / war3mapUnits.doo 的 Godot 桥：解析在 W3Compat.Core，
/// 坐标转为 Godot 轴（W3 Y→Godot Z，W3 Z→Godot Y）。
/// 特殊地形装饰物按 HiveWE（version+count+id+variation+ivec2）保留在 trailer，
/// 不经错误的 float 展开；需地图尺寸时用 ExpandSpecialDoodads。
/// </summary>
[GlobalClass]
public partial class W3DooParserBridge : RefCounted
{
    /// <summary>最近一次解析的警告/错误（空表示无）。</summary>
    public static string LastError { get; private set; } = "";

    public static string last_error
    {
        get => LastError;
        set => LastError = value ?? "";
    }

    /// <summary>游戏版本是否启用 skin_id（≥1.32）。</summary>
    public static bool HasSkinIds(int major, int minor) =>
        major * 100 + minor >= 132;

    public static bool has_skin_ids(int major, int minor) => HasSkinIds(major, minor);

    /// <summary>解析装饰物主表为 W3PlacementData 列表（不含错误 float 特化段）。</summary>
    public static GArray ParseDoodads(byte[]? data, int gameVersionMajor = 0, int gameVersionMinor = 0)
    {
        LastError = "";
        var outList = new GArray();
        if (data == null || data.Length == 0)
            return outList;
        if (data.Length < 12)
        {
            LastError = "doodad file too short";
            return outList;
        }

        bool hasSkin = HasSkinIds(gameVersionMajor, gameVersionMinor);
        if (!LooksLikeW3do(data))
        {
            LastError = $"doodad magic {ReadFourcc(data)}";
            GD.PushWarning($"W3DooParser: doodad magic {ReadFourcc(data)}");
            return outList;
        }

        W3DooDocument doc = W3DooFile.ParseDoodadsDocument(data, hasSkin);
        foreach (W3Placement p in doc.Placements)
            outList.Add(ToGodotPlacement(p));

        int specialHint = CountSpecialTrailer(doc.SpecialTrailer);
        if (specialHint > 0)
        {
            GD.Print($"W3DooParser: special/terrain doodads trailer count={specialHint} (expand via ExpandSpecialDoodads)");
        }

        GD.Print($"W3DooParser: doodads={outList.Count}/{doc.Placements.Count} ver={doc.FileVersion} skins={hasSkin}");
        return outList;
    }

    public static GArray parse_doodads(byte[]? data, int gameVersionMajor = 0, int gameVersionMinor = 0) =>
        ParseDoodads(data, gameVersionMajor, gameVersionMinor);

    /// <summary>解析单位表为 W3PlacementData 列表。</summary>
    public static GArray ParseUnits(byte[]? data, int gameVersionMajor = 0, int gameVersionMinor = 0)
    {
        LastError = "";
        var outList = new GArray();
        if (data == null || data.Length == 0)
            return outList;
        if (data.Length < 12)
        {
            LastError = "units file too short";
            return outList;
        }

        bool hasSkin = HasSkinIds(gameVersionMajor, gameVersionMinor);
        if (!LooksLikeW3do(data))
        {
            LastError = $"units magic {ReadFourcc(data)}";
            GD.PushWarning($"W3DooParser: units magic {ReadFourcc(data)}");
            return outList;
        }

        W3DooDocument doc = W3DooFile.ParseUnitsDocument(data, hasSkin);
        foreach (W3Placement p in doc.Placements)
            outList.Add(ToGodotPlacement(p));

        GD.Print($"W3DooParser: units={outList.Count} ver={doc.FileVersion} sub={doc.Subversion} skins={hasSkin}");
        return outList;
    }

    public static GArray parse_units(byte[]? data, int gameVersionMajor = 0, int gameVersionMinor = 0) =>
        ParseUnits(data, gameVersionMajor, gameVersionMinor);

    /// <summary>
    /// 按 HiveWE 规则展开 special trailer 为放置（瓦片坐标→世界坐标）。
    /// getCornerWorldHeight 签名：(cornerX, cornerY) → 世界高度。
    /// </summary>
    public static GArray ExpandSpecialDoodads(
        byte[]? doodadBytes,
        bool hasSkinIds,
        int terrainWidth,
        int terrainHeight,
        float centerOffsetX,
        float centerOffsetY,
        Callable getCornerWorldHeight,
        float tileSize = 128f)
    {
        var outList = new GArray();
        if (doodadBytes == null || doodadBytes.Length < 12)
            return outList;
        W3DooDocument doc = W3DooFile.ParseDoodadsDocument(doodadBytes, hasSkinIds);
        try
        {
            IReadOnlyList<W3Placement> specials = doc.ReadSpecialPlacements(
                terrainWidth,
                terrainHeight,
                centerOffsetX,
                centerOffsetY,
                (cx, cy) => (float)getCornerWorldHeight.Call(cx, cy).AsDouble(),
                tileSize);
            foreach (W3Placement p in specials)
                outList.Add(ToGodotPlacement(p));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            GD.PushWarning($"W3DooParser: special expand failed: {ex.Message}");
        }
        return outList;
    }

    public static GArray expand_special_doodads(
        byte[]? doodadBytes,
        bool hasSkinIds,
        int terrainWidth,
        int terrainHeight,
        float centerOffsetX,
        float centerOffsetY,
        Callable getCornerWorldHeight,
        float tileSize = 128f) =>
        ExpandSpecialDoodads(
            doodadBytes, hasSkinIds, terrainWidth, terrainHeight,
            centerOffsetX, centerOffsetY, getCornerWorldHeight, tileSize);

    /// <summary>W3 世界坐标放置 → Godot 轴 + W3PlacementData。</summary>
    public static W3PlacementData ToGodotPlacement(W3Placement p)
    {
        // W3 (X,Y,Z) → Godot (X, Z, Y)；缩放同理 ScaleX/ScaleZ/ScaleY
        return new W3PlacementData
        {
            Id = p.Id,
            Variation = p.Variation,
            Position = new Vector3(p.X, p.Z, p.Y),
            Angle = p.Angle,
            Scale = new Vector3(p.ScaleX, p.ScaleZ, p.ScaleY),
            SkinId = string.IsNullOrEmpty(p.SkinId) ? p.Id : p.SkinId,
            Flags = p.Flags,
            PlayerOwner = p.PlayerOwner,
            Life = p.Life,
            IsUnit = p.IsUnit,
            CreationNumber = p.CreationNumber,
        };
    }

    private static bool LooksLikeW3do(byte[] data) =>
        data.Length >= 4 && ReadFourcc(data) == "W3do";

    private static string ReadFourcc(byte[] data) =>
        data.Length < 4 ? "" : System.Text.Encoding.ASCII.GetString(data, 0, 4);

    /// <summary>粗读 trailer：HiveWE 为 version(u32)+count(u32)+records。</summary>
    private static int CountSpecialTrailer(byte[] trailer)
    {
        if (trailer.Length < 8)
            return 0;
        int version = BitConverter.ToInt32(trailer, 0);
        if (version != 0)
            return 0;
        int count = BitConverter.ToInt32(trailer, 4);
        if (count < 0 || count > 50000)
            return 0;
        if (trailer.Length != 8 + count * 16)
            return 0;
        return count;
    }
}
