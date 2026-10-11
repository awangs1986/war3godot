using Godot;
using W3Compat.Formats;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Formats;

/// <summary>将 W3MapInfoVersion 暴露给 GDScript。</summary>
[GlobalClass]
public partial class W3MapInfoVersionBridge : RefCounted
{
    public static Godot.Collections.Dictionary ParseStatic(byte[] data) =>
        new W3MapInfoVersionBridge().Parse(data);

    public static bool HasReforgedSkinIdsStatic(Godot.Collections.Dictionary info) =>
        new W3MapInfoVersionBridge().HasReforgedSkinIds(info);

    public GDictionary Parse(byte[] data)
    {
        W3MapInfoVersion info = W3MapInfoVersion.Parse(data ?? System.Array.Empty<byte>());
        return new GDictionary
        {
            ["format_version"] = info.FormatVersion,
            ["map_version"] = info.MapVersion,
            ["editor_version"] = info.EditorVersion,
            ["game_version_major"] = info.GameVersionMajor,
            ["game_version_minor"] = info.GameVersionMinor,
            ["game_version_patch"] = info.GameVersionPatch,
            ["game_version_build"] = info.GameVersionBuild,
            ["valid"] = info.Valid,
        };
    }

    public GDictionary parse(byte[] data) => Parse(data);

    public bool HasReforgedSkinIds(GDictionary info)
    {
        int major = info.ContainsKey("game_version_major") ? info["game_version_major"].AsInt32() : 0;
        int minor = info.ContainsKey("game_version_minor") ? info["game_version_minor"].AsInt32() : 0;
        return major * 100 + minor >= 132;
    }

    public bool has_reforged_skin_ids(GDictionary info) => HasReforgedSkinIds(info);
}
