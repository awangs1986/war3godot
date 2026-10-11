namespace W3Editor.Formats;

/// <summary>
/// Minimal war3map.w3i header — port of addons/w3kit/w3_map_info_version.gd
/// (field order mirrors Warsmash War3MapW3i / HiveWE MapInfo).
/// </summary>
public sealed class W3MapInfoVersion
{
    public int FormatVersion;
    public int MapVersion;
    public int EditorVersion;
    public int GameVersionMajor;
    public int GameVersionMinor;
    public int GameVersionPatch;
    public int GameVersionBuild;
    public bool Valid;

    /// <summary>Reforged (1.32+) maps store extra skin-id fields in doo/units files.</summary>
    public bool HasReforgedSkinIds => GameVersionMajor * 100 + GameVersionMinor >= 132;

    public static W3MapInfoVersion Parse(byte[] data)
    {
        var r = new W3MapInfoVersion();
        if (data.Length < 4) return r;
        int p = 0;
        int I32() { var v = BitConverter.ToInt32(data, p); p += 4; return v; }
        r.FormatVersion = I32();
        if (r.FormatVersion >= 18)
        {
            if (data.Length - p < 8) return r;
            r.MapVersion = I32();
            r.EditorVersion = I32();
        }
        if (r.FormatVersion >= 28)
        {
            if (data.Length - p < 16) return r;
            r.GameVersionMajor = I32();
            r.GameVersionMinor = I32();
            r.GameVersionPatch = I32();
            r.GameVersionBuild = I32();
        }
        r.Valid = true;
        return r;
    }
}
