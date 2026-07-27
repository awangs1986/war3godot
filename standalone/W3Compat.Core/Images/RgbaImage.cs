namespace W3Compat.Images;

/// <summary>与引擎无关的 RGBA8 位图缓冲。</summary>
public sealed class RgbaImage
{
    /// <summary>像素宽度。</summary>
    public int Width { get; }

    /// <summary>像素高度。</summary>
    public int Height { get; }

    /// <summary>按行主序交错存放的 RGBA 字节。</summary>
    public byte[] Rgba { get; }

    /// <summary>创建指定尺寸的空图像（默认透明黑）。</summary>
    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "图像尺寸必须为正");
        Width = width;
        Height = height;
        Rgba = new byte[checked(width * height * 4)];
    }

    /// <summary>用已有缓冲包装；长度必须为 width*height*4。</summary>
    public RgbaImage(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA 缓冲长度与尺寸不匹配", nameof(rgba));
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>写入单个像素（RGBA 各 0–255）。</summary>
    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = (y * Width + x) * 4;
        Rgba[i] = r;
        Rgba[i + 1] = g;
        Rgba[i + 2] = b;
        Rgba[i + 3] = a;
    }
}
