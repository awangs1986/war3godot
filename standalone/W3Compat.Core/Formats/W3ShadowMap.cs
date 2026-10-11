namespace W3Compat.Formats;

/// <summary>
/// war3map.shd 烘焙阴影图：尺寸 = (tiles_x*4)*(tiles_y*4)，
/// 原始字节 0x00=无阴影、0xFF=全阴影；上传时按 open-realm 约定右移 1 位。
/// </summary>
public static class W3ShadowMap
{
    /// <summary>按瓦片数计算 SHD 期望字节长度。</summary>
    public static int ExpectedSize(int tilesX, int tilesY)
    {
        if (tilesX <= 0 || tilesY <= 0)
            return 0;
        return checked(tilesX * 4 * tilesY * 4);
    }

    /// <summary>
    /// 将原始 SHD 字节打包为 R8 采样缓冲（shadow/2）。
    /// 长度不足时返回 null。
    /// </summary>
    public static byte[]? PackR8Half(ReadOnlySpan<byte> bytes, int tilesX, int tilesY)
    {
        int need = ExpectedSize(tilesX, tilesY);
        if (need <= 0 || bytes.Length < need)
            return null;

        var packed = new byte[need];
        for (int i = 0; i < need; i++)
            packed[i] = (byte)(bytes[i] >> 1); // open-realm：alpha = shadow/2
        return packed;
    }

    /// <summary>像素宽（每瓦片 4 个阴影样本）。</summary>
    public static int PixelWidth(int tilesX) => Math.Max(0, tilesX) * 4;

    /// <summary>像素高（每瓦片 4 个阴影样本）。</summary>
    public static int PixelHeight(int tilesY) => Math.Max(0, tilesY) * 4;
}
