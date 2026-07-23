using SkiaSharp;

namespace W3Editor.Rendering;

/// <summary>CPU-side RGBA8 bitmap used between asset decode and GL upload.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public static RgbaImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = a;
        }
        return new RgbaImage(width, height, pixels);
    }

    public static RgbaImage Checkerboard(int size = 64)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool on = ((x >> 3) + (y >> 3)) % 2 == 0;
            int i = (y * size + x) * 4;
            pixels[i] = on ? (byte)102 : (byte)77;
            pixels[i + 1] = on ? (byte)140 : (byte)107;
            pixels[i + 2] = on ? (byte)77 : (byte)56;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(size, size, pixels);
    }

    /// <summary>Loads .png (SkiaSharp) or .dds (built-in BC decoder). Null when unreadable.</summary>
    public static RgbaImage? LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            byte[] bytes = File.ReadAllBytes(path);
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                return DdsDecoder.Decode(bytes);
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap == null)
                return null;
            using var converted = bitmap.ColorType == SKColorType.Rgba8888
                ? null
                : bitmap.Copy(SKColorType.Rgba8888);
            SKBitmap source = converted ?? bitmap;
            var pixels = new byte[source.Width * source.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), pixels, 0, pixels.Length);
            return new RgbaImage(source.Width, source.Height, pixels);
        }
        catch
        {
            return null;
        }
    }

    public RgbaImage Resized(int width, int height)
    {
        if (width == Width && height == Height)
            return this;
        var output = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) * Height / height - 0.5f;
            int v0 = Math.Clamp((int)MathF.Floor(v), 0, Height - 1);
            int v1 = Math.Min(v0 + 1, Height - 1);
            float fv = Math.Clamp(v - v0, 0f, 1f);
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) * Width / width - 0.5f;
                int u0 = Math.Clamp((int)MathF.Floor(u), 0, Width - 1);
                int u1 = Math.Min(u0 + 1, Width - 1);
                float fu = Math.Clamp(u - u0, 0f, 1f);
                int di = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float p00 = Pixels[(v0 * Width + u0) * 4 + c];
                    float p10 = Pixels[(v0 * Width + u1) * 4 + c];
                    float p01 = Pixels[(v1 * Width + u0) * 4 + c];
                    float p11 = Pixels[(v1 * Width + u1) * 4 + c];
                    float top = p00 + (p10 - p00) * fu;
                    float bottom = p01 + (p11 - p01) * fu;
                    output[di + c] = (byte)Math.Clamp((int)MathF.Round(top + (bottom - top) * fv), 0, 255);
                }
            }
        }
        return new RgbaImage(width, height, output);
    }
}
