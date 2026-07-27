using System.Buffers.Binary;
using System.Text;
using W3Compat.IO;

namespace W3Compat.Images;

/// <summary>
/// BLP1/BLP2 解码器（纯 C#）。JPEG-CMYK 路径不在此处理——由离线 sidecar 或 Godot 适配层负责。
/// </summary>
public static class BlpDecoder
{
    public const int MaxDimension = 8192;
    public const int MaxPixels = 64 * 1024 * 1024;

    /// <summary>从完整 BLP 文件字节解码；失败返回 null。</summary>
    public static RgbaImage? DecodeBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return null;
        string magic = Encoding.ASCII.GetString(data[..4]);
        return magic switch
        {
            "BLP1" => DecodeBlp1(data),
            "BLP2" => DecodeBlp2(data),
            _ => null,
        };
    }

    /// <summary>BLP 是否声明了 alpha（用于 sidecar 选择）。</summary>
    public static bool DeclaresAlpha(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return true;
        string magic = Encoding.ASCII.GetString(data[..4]);
        if (magic == "BLP1")
            return BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != 0;
        if (magic == "BLP2")
            return data.Length > 9 && data[9] != 0;
        return true;
    }

    /// <summary>
    /// 提取 BLP1 JPEG 内容（header+body 拼接后的字节流）；仅当 content==0（JPEG）时成功。
    /// War3 JPEG-BLP 常为 4 通道“假 CMYK”，纯核心不解码——留给 Godot 适配层判定
    /// 组件数后再决定走原生 JPEG 解码还是 sidecar 兜底。
    /// </summary>
    public static bool TryExtractBlp1Jpeg(ReadOnlySpan<byte> data, out int width, out int height, out byte[]? jpeg)
    {
        width = 0;
        height = 0;
        jpeg = null;
        if (data.Length < 8 || Encoding.ASCII.GetString(data[..4]) != "BLP1")
            return false;

        var r = new BinarySpanReader(data);
        r.ReadFourCC();
        uint content = r.ReadU32();
        if (content != 0)
            return false;
        r.ReadU32(); // alpha_bits：JPEG 内容不使用
        width = (int)r.ReadU32();
        height = (int)r.ReadU32();
        r.ReadU32(); // extra
        r.ReadU32(); // has_mipmaps
        Span<uint> mipOffsets = stackalloc uint[16];
        Span<uint> mipSizes = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
            mipOffsets[i] = r.ReadU32();
        for (int i = 0; i < 16; i++)
            mipSizes[i] = r.ReadU32();
        if (!ValidDimensions(width, height) || mipSizes[0] == 0)
            return false;
        int off = (int)mipOffsets[0];
        int sz = (int)mipSizes[0];
        if (off < 0 || sz <= 0 || (long)off + sz > data.Length)
            return false;

        const int hdrOff = 156;
        if (hdrOff + 4 > data.Length)
            return false;
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[hdrOff..]);
        if (headerSize == 0 || (long)hdrOff + 4 + headerSize > data.Length)
            return false;

        var jpg = new byte[headerSize + sz];
        data.Slice(hdrOff + 4, (int)headerSize).CopyTo(jpg);
        data.Slice(off, sz).CopyTo(jpg.AsSpan((int)headerSize));
        jpeg = jpg;
        return true;
    }

    /// <summary>BLP1 是否为无 PNG sidecar 时无法回退的 4 通道 JPEG-CMYK。</summary>
    public static bool IsCmykJpegBlp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 160 || Encoding.ASCII.GetString(data[..4]) != "BLP1")
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0) // content: 0 = JPEG
            return false;
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[156..]);
        if (headerSize == 0 || 160 + headerSize > data.Length)
            return false;
        uint mipOff = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        uint mipSize = BinaryPrimitives.ReadUInt32LittleEndian(data[92..]); // mip_sizes[0] = 28 + 16*4
        if ((long)mipOff + mipSize > data.Length)
            return false;
        var jpg = new byte[headerSize + mipSize];
        data.Slice(160, (int)headerSize).CopyTo(jpg);
        data.Slice((int)mipOff, (int)mipSize).CopyTo(jpg.AsSpan((int)headerSize));
        return JpegComponentCount(jpg) == 4;
    }

    /// <summary>从 SOFn 段读取 JPEG 通道数；非法/截断数据返回 0。</summary>
    public static int JpegComponentCount(ReadOnlySpan<byte> jpg)
    {
        if (jpg.Length < 12 || jpg[0] != 0xFF || jpg[1] != 0xD8)
            return 0;
        int pos = 2;
        while (pos + 3 < jpg.Length)
        {
            while (pos < jpg.Length && jpg[pos] == 0xFF)
                pos++;
            if (pos >= jpg.Length)
                break;
            byte marker = jpg[pos];
            pos++;
            if (marker == 0xD9 || marker == 0xDA)
                break;
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue;
            if (pos + 1 >= jpg.Length)
                break;
            int segmentSize = (jpg[pos] << 8) | jpg[pos + 1];
            if (segmentSize < 2 || pos + segmentSize > jpg.Length)
                break;
            bool isSof =
                (marker >= 0xC0 && marker <= 0xC3)
                || (marker >= 0xC5 && marker <= 0xC7)
                || (marker >= 0xC9 && marker <= 0xCB)
                || (marker >= 0xCD && marker <= 0xCF);
            if (isSof && segmentSize >= 8)
                return jpg[pos + 7];
            pos += segmentSize;
        }
        return 0;
    }

    private static RgbaImage? DecodeBlp1(ReadOnlySpan<byte> data)
    {
        var r = new BinarySpanReader(data);
        r.ReadFourCC();
        uint content = r.ReadU32();
        uint alphaBits = r.ReadU32();
        int width = (int)r.ReadU32();
        int height = (int)r.ReadU32();
        r.ReadU32();
        r.ReadU32();
        Span<uint> mipOffsets = stackalloc uint[16];
        Span<uint> mipSizes = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
            mipOffsets[i] = r.ReadU32();
        for (int i = 0; i < 16; i++)
            mipSizes[i] = r.ReadU32();
        if (!ValidDimensions(width, height) || mipSizes[0] == 0)
            return null;
        int off = (int)mipOffsets[0];
        int sz = (int)mipSizes[0];
        if (off < 0 || sz <= 0 || (long)off + sz > data.Length)
            return null;

        if (content == 0)
        {
            // JPEG：纯核心不解码（含 CMYK）；返回 null 让上层走 sidecar / Godot
            return null;
        }

        // 调色板
        const int palPos = 156;
        if (data.Length < palPos + 256 * 4)
            return MakeChecker(width, height);
        int pixN = width * height;
        if (off + pixN > data.Length)
            return MakeChecker(width, height);

        var img = new RgbaImage(width, height);
        ReadOnlySpan<byte> indices = data.Slice(off, pixN);
        for (int i = 0; i < pixN; i++)
        {
            int idx = indices[i];
            int p = palPos + idx * 4;
            byte b = data[p];
            byte g = data[p + 1];
            byte r8 = data[p + 2];
            img.SetPixel(i % width, i / width, r8, g, b, 255);
        }
        if (alphaBits > 0)
            ApplyPaletteAlpha(img, data, off + pixN, (int)alphaBits);
        return img;
    }

    private static void ApplyPaletteAlpha(RgbaImage img, ReadOnlySpan<byte> data, int alphaOff, int alphaBits)
    {
        int n = img.Width * img.Height;
        for (int i = 0; i < n; i++)
        {
            byte a = 255;
            switch (alphaBits)
            {
                case 1:
                {
                    int bi = alphaOff + i / 8;
                    if (bi >= data.Length) return;
                    a = (byte)(((data[bi] >> (i % 8)) & 1) != 0 ? 255 : 0);
                    break;
                }
                case 4:
                {
                    int ni = alphaOff + i / 2;
                    if (ni >= data.Length) return;
                    int nibble = (i % 2) == 0 ? (data[ni] & 0x0F) : ((data[ni] >> 4) & 0x0F);
                    a = (byte)(nibble * 17);
                    break;
                }
                case 8:
                {
                    int ai = alphaOff + i;
                    if (ai >= data.Length) return;
                    a = data[ai];
                    break;
                }
            }
            int pix = i * 4;
            img.Rgba[pix + 3] = a;
        }
    }

    private static RgbaImage? DecodeBlp2(ReadOnlySpan<byte> data)
    {
        var r = new BinarySpanReader(data);
        r.ReadFourCC();
        r.ReadU32();
        byte compression = r.ReadU8();
        byte alphaDepth = r.ReadU8();
        r.ReadU8(); // alpha_type
        r.ReadU8();
        int width = (int)r.ReadU32();
        int height = (int)r.ReadU32();
        if (!ValidDimensions(width, height))
            return null;
        Span<uint> offsets = stackalloc uint[16];
        Span<uint> sizes = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
            offsets[i] = r.ReadU32();
        for (int i = 0; i < 16; i++)
            sizes[i] = r.ReadU32();
        if (sizes[0] == 0 || (long)offsets[0] + sizes[0] > data.Length)
            return MakeChecker(Math.Max(1, width), Math.Max(1, height));
        ReadOnlySpan<byte> block = data.Slice((int)offsets[0], (int)sizes[0]);
        if (compression == 1)
        {
            var img = new RgbaImage(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (i + 3 >= block.Length) return img;
                img.SetPixel(x, y, block[i + 2], block[i + 1], block[i], block[i + 3]);
            }
            return img;
        }
        // BLP2 的 compression=2 使用 DXT1/3/5；alpha depth 决定具体格式。
        if (compression == 2)
            return DecodeDxt(block, width, height, alphaDepth);
        return MakeChecker(width, height);
    }

    /// <summary>解码 BLP2 内嵌的 DXT 块；边缘不足 4 像素的块会安全裁切。</summary>
    private static RgbaImage? DecodeDxt(ReadOnlySpan<byte> block, int width, int height, byte alphaDepth)
    {
        int bytesPerBlock = alphaDepth switch
        {
            0 or 1 => 8, // DXT1（1-bit alpha 也由端点顺序表示）
            4 => 16,     // DXT3
            8 => 16,     // DXT5
            _ => 0,
        };
        int blocksWide = (width + 3) / 4;
        int blocksHigh = (height + 3) / 4;
        if (bytesPerBlock == 0 || (long)blocksWide * blocksHigh * bytesPerBlock > block.Length)
            return null;

        var image = new RgbaImage(width, height);
        int offset = 0;
        for (int by = 0; by < blocksHigh; by++)
        for (int bx = 0; bx < blocksWide; bx++)
        {
            ReadOnlySpan<byte> source = block.Slice(offset, bytesPerBlock);
            offset += bytesPerBlock;
            if (alphaDepth == 4)
                DecodeDxt3Block(source, image, bx * 4, by * 4);
            else if (alphaDepth == 8)
                DecodeDxt5Block(source, image, bx * 4, by * 4);
            else
                DecodeDxt1Block(source, image, bx * 4, by * 4, alphaDepth == 1);
        }
        return image;
    }

    /// <summary>解码 8 字节 DXT1 色块。</summary>
    private static void DecodeDxt1Block(ReadOnlySpan<byte> block, RgbaImage image, int baseX, int baseY, bool allowTransparent)
    {
        DecodeDxtColors(block, image, baseX, baseY, 0, allowTransparent, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>解码 16 字节 DXT3 块，前 8 字节是显式 4-bit alpha。</summary>
    private static void DecodeDxt3Block(ReadOnlySpan<byte> block, RgbaImage image, int baseX, int baseY)
    {
        Span<byte> alpha = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
        {
            int nibble = (block[i / 2] >> ((i & 1) * 4)) & 0x0F;
            alpha[i] = (byte)(nibble * 17);
        }
        DecodeDxtColors(block, image, baseX, baseY, 8, false, alpha);
    }

    /// <summary>解码 16 字节 DXT5 块，前 2 字节定义插值 alpha 调色板。</summary>
    private static void DecodeDxt5Block(ReadOnlySpan<byte> block, RgbaImage image, int baseX, int baseY)
    {
        Span<byte> alphaPalette = stackalloc byte[8];
        alphaPalette[0] = block[0];
        alphaPalette[1] = block[1];
        if (alphaPalette[0] > alphaPalette[1])
        {
            for (int i = 1; i <= 6; i++)
                alphaPalette[i + 1] = (byte)(((7 - i) * alphaPalette[0] + i * alphaPalette[1]) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                alphaPalette[i + 1] = (byte)(((5 - i) * alphaPalette[0] + i * alphaPalette[1]) / 5);
            alphaPalette[6] = 0;
            alphaPalette[7] = 255;
        }

        ulong packed = 0;
        for (int i = 0; i < 6; i++)
            packed |= (ulong)block[i + 2] << (i * 8);
        Span<byte> alpha = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
            alpha[i] = alphaPalette[(int)((packed >> (i * 3)) & 7)];
        DecodeDxtColors(block, image, baseX, baseY, 8, false, alpha);
    }

    /// <summary>解码 DXT 公用 RGB565 调色板并写入像素。</summary>
    private static void DecodeDxtColors(ReadOnlySpan<byte> block, RgbaImage image, int baseX, int baseY, int colorOffset, bool allowTransparent, ReadOnlySpan<byte> alpha)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block[colorOffset..]);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[(colorOffset + 2)..]);
        Span<(byte R, byte G, byte B, byte A)> colors = stackalloc (byte, byte, byte, byte)[4];
        colors[0] = ExpandRgb565(c0);
        colors[1] = ExpandRgb565(c1);
        if (!allowTransparent || c0 > c1)
        {
            colors[2] = Mix(colors[0], colors[1], 2, 1, 3);
            colors[3] = Mix(colors[0], colors[1], 1, 2, 3);
        }
        else
        {
            colors[2] = Mix(colors[0], colors[1], 1, 1, 2);
            colors[3] = (0, 0, 0, 0);
        }

        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[(colorOffset + 4)..]);
        for (int i = 0; i < 16; i++)
        {
            int x = baseX + (i & 3);
            int y = baseY + (i >> 2);
            if (x >= image.Width || y >= image.Height)
                continue;
            var color = colors[(int)((indices >> (i * 2)) & 3)];
            byte a = alpha.IsEmpty ? color.A : alpha[i];
            image.SetPixel(x, y, color.R, color.G, color.B, a);
        }
    }

    /// <summary>把 RGB565 展开为 8-bit RGBA。</summary>
    private static (byte R, byte G, byte B, byte A) ExpandRgb565(ushort color) =>
        ((byte)(((color >> 11) & 31) * 255 / 31),
         (byte)(((color >> 5) & 63) * 255 / 63),
         (byte)((color & 31) * 255 / 31), 255);

    /// <summary>按整数权重混合两个 DXT 颜色。</summary>
    private static (byte R, byte G, byte B, byte A) Mix(
        (byte R, byte G, byte B, byte A) a,
        (byte R, byte G, byte B, byte A) b,
        int aw, int bw, int total) =>
        ((byte)((a.R * aw + b.R * bw) / total),
         (byte)((a.G * aw + b.G * bw) / total),
         (byte)((a.B * aw + b.B * bw) / total), 255);

    private static RgbaImage MakeChecker(int w, int h)
    {
        var img = new RgbaImage(Math.Max(1, w), Math.Max(1, h));
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
        {
            bool on = ((x >> 3) + (y >> 3)) % 2 == 0;
            if (on) img.SetPixel(x, y, 89, 140, 71);
            else img.SetPixel(x, y, 64, 102, 51);
        }
        return img;
    }

    /// <summary>宽高与像素总数是否在安全解码范围内（拒绝异常巨图分配）。</summary>
    public static bool ValidDimensions(int width, int height) =>
        width > 0 && height > 0
        && width <= MaxDimension && height <= MaxDimension
        && (long)width * height <= MaxPixels;
}
