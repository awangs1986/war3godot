using System.Buffers.Binary;

namespace W3Editor.Rendering;

/// <summary>
/// Minimal DDS reader for Reforged terrain assets: BC1 (DXT1), BC2 (DXT3),
/// BC3 (DXT5) and uncompressed 32-bit, top mip only, decoded to RGBA8.
/// Deterministic CPU decode — no reliance on GL S3TC extensions.
/// </summary>
public static class DdsDecoder
{
    public static RgbaImage? Decode(byte[] file)
    {
        if (file.Length < 128 || BinaryPrimitives.ReadUInt32LittleEndian(file) != 0x20534444) // "DDS "
            return null;
        int height = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(12));
        int width = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(16));
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            return null;
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(80));
        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(84));
        int dataOffset = 128;
        if (fourCc == 0x30315844) // "DX10" extended header
        {
            uint dxgiFormat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(128));
            dataOffset = 148;
            fourCc = dxgiFormat switch
            {
                70 or 71 or 72 => 0x31545844u, // BC1
                73 or 74 or 75 => 0x33545844u, // BC2
                76 or 77 or 78 => 0x35545844u, // BC3
                _ => 0u,
            };
            if (fourCc == 0)
                return null;
        }

        var pixels = new byte[width * height * 4];
        if ((pfFlags & 0x4) != 0) // DDPF_FOURCC
        {
            switch (fourCc)
            {
                case 0x31545844: DecodeBc(file.AsSpan(dataOffset), width, height, pixels, bc: 1); break;
                case 0x33545844: DecodeBc(file.AsSpan(dataOffset), width, height, pixels, bc: 2); break;
                case 0x35545844: DecodeBc(file.AsSpan(dataOffset), width, height, pixels, bc: 3); break;
                default: return null;
            }
            return new RgbaImage(width, height, pixels);
        }

        uint rgbBitCount = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(88));
        if ((pfFlags & 0x40) != 0 && rgbBitCount == 32) // DDPF_RGB, assume BGRA masks
        {
            uint rMask = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(92));
            bool bgra = rMask == 0x00FF0000;
            var src = file.AsSpan(dataOffset);
            if (src.Length < width * height * 4)
                return null;
            for (int i = 0; i < width * height; i++)
            {
                pixels[i * 4 + 0] = bgra ? src[i * 4 + 2] : src[i * 4 + 0];
                pixels[i * 4 + 1] = src[i * 4 + 1];
                pixels[i * 4 + 2] = bgra ? src[i * 4 + 0] : src[i * 4 + 2];
                pixels[i * 4 + 3] = (pfFlags & 0x1) != 0 ? src[i * 4 + 3] : (byte)255;
            }
            return new RgbaImage(width, height, pixels);
        }
        return null;
    }

    private static void DecodeBc(ReadOnlySpan<byte> data, int width, int height, byte[] output, int bc)
    {
        int blockBytes = bc == 1 ? 8 : 16;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        Span<byte> block = stackalloc byte[64]; // 4x4 RGBA
        int offset = 0;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (offset + blockBytes > data.Length)
                    return;
                ReadOnlySpan<byte> src = data.Slice(offset, blockBytes);
                offset += blockBytes;
                switch (bc)
                {
                    case 1: DecodeColorBlock(src, block, opaqueMode: false); break;
                    case 2:
                        DecodeColorBlock(src[8..], block, opaqueMode: true);
                        for (int i = 0; i < 16; i++)
                        {
                            int nibble = (src[i / 2] >> ((i % 2) * 4)) & 0xF;
                            block[i * 4 + 3] = (byte)(nibble * 17);
                        }
                        break;
                    case 3:
                        DecodeColorBlock(src[8..], block, opaqueMode: true);
                        DecodeAlphaBlock(src, block);
                        break;
                }
                int copyW = Math.Min(4, width - bx * 4);
                int copyH = Math.Min(4, height - by * 4);
                for (int y = 0; y < copyH; y++)
                {
                    int dst = ((by * 4 + y) * width + bx * 4) * 4;
                    block.Slice(y * 16, copyW * 4).CopyTo(output.AsSpan(dst));
                }
            }
        }
    }

    private static void DecodeColorBlock(ReadOnlySpan<byte> src, Span<byte> block, bool opaqueMode)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(src);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(src[2..]);
        Span<byte> palette = stackalloc byte[16];
        Expand565(c0, palette);
        Expand565(c1, palette[4..]);
        if (c0 > c1 || opaqueMode)
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((2 * palette[i] + palette[4 + i]) / 3);
                palette[12 + i] = (byte)((palette[i] + 2 * palette[4 + i]) / 3);
            }
            palette[11] = 255;
            palette[15] = 255;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((palette[i] + palette[4 + i]) / 2);
                palette[12 + i] = 0;
            }
            palette[11] = 255;
            palette[15] = 0; // transparent black
        }
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        for (int i = 0; i < 16; i++)
        {
            int index = (int)((indices >> (i * 2)) & 0x3) * 4;
            block[i * 4 + 0] = palette[index];
            block[i * 4 + 1] = palette[index + 1];
            block[i * 4 + 2] = palette[index + 2];
            block[i * 4 + 3] = palette[index + 3];
        }
    }

    private static void Expand565(ushort color, Span<byte> rgba)
    {
        int r = (color >> 11) & 0x1F;
        int g = (color >> 5) & 0x3F;
        int b = color & 0x1F;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    private static void DecodeAlphaBlock(ReadOnlySpan<byte> src, Span<byte> block)
    {
        Span<byte> alpha = stackalloc byte[8];
        alpha[0] = src[0];
        alpha[1] = src[1];
        if (alpha[0] > alpha[1])
        {
            for (int i = 1; i < 7; i++)
                alpha[1 + i] = (byte)(((7 - i) * alpha[0] + i * alpha[1]) / 7);
        }
        else
        {
            for (int i = 1; i < 5; i++)
                alpha[1 + i] = (byte)(((5 - i) * alpha[0] + i * alpha[1]) / 5);
            alpha[6] = 0;
            alpha[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++)
            bits |= (ulong)src[2 + i] << (i * 8);
        for (int i = 0; i < 16; i++)
            block[i * 4 + 3] = alpha[(int)((bits >> (i * 3)) & 0x7)];
    }
}
