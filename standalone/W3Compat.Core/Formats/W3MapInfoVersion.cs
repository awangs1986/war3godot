using W3Compat.IO;

namespace W3Compat.Formats;

/// <summary>war3map.w3i 的最小版本头信息。</summary>
public sealed class W3MapInfoVersion
{
    /// <summary>W3I 文件格式版本。</summary>
    public int FormatVersion;

    /// <summary>地图版本号。</summary>
    public int MapVersion;

    /// <summary>地图编辑器版本号。</summary>
    public int EditorVersion;

    /// <summary>目标游戏主版本号。</summary>
    public int GameVersionMajor;

    /// <summary>目标游戏次版本号。</summary>
    public int GameVersionMinor;

    /// <summary>目标游戏补丁版本号。</summary>
    public int GameVersionPatch;

    /// <summary>目标游戏构建版本号。</summary>
    public int GameVersionBuild;

    /// <summary>头信息是否完整有效。</summary>
    public bool Valid;

    /// <summary>Reforged 1.32+ 地图的 DOO/单位文件是否包含皮肤原始码。</summary>
    public bool HasReforgedSkinIds => GameVersionMajor * 100 + GameVersionMinor >= 132;

    /// <summary>解析 war3map.w3i 文件开头的版本字段。</summary>
    public static W3MapInfoVersion Parse(byte[] data)
    {
        var result = new W3MapInfoVersion();
        if (data.Length < sizeof(int))
            return result;

        var reader = new BinarySpanReader(data);
        result.FormatVersion = reader.ReadI32();
        if (result.FormatVersion >= 18)
        {
            if (reader.Remaining < 8)
                return result;
            result.MapVersion = reader.ReadI32();
            result.EditorVersion = reader.ReadI32();
        }

        if (result.FormatVersion >= 28)
        {
            if (reader.Remaining < 16)
                return result;
            result.GameVersionMajor = reader.ReadI32();
            result.GameVersionMinor = reader.ReadI32();
            result.GameVersionPatch = reader.ReadI32();
            result.GameVersionBuild = reader.ReadI32();
        }

        result.Valid = true;
        return result;
    }
}
