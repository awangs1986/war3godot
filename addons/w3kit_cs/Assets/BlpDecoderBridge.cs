using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using W3Compat.Images;

namespace Diablo1Like.W3.Assets;

/// <summary>
/// BLP 解码 + PNG/DDS sidecar 加载的 Godot 适配层。
/// 语义解码（调色板/DXT/alpha）全部在 W3Compat.Core.Images.BlpDecoder；
/// 本类只负责文件 I/O、Godot Image 转换与 BLP1 3 通道 JPEG 的原生解码兜底
/// （4 通道“假 CMYK”JPEG 纯核心不支持，留给离线 sidecar 转换）。
/// </summary>
[GlobalClass]
public partial class BlpDecoderBridge : RefCounted
{
    /// <summary>直接可被 Godot Image 加载的扩展名（无需 BLP/PNG 配对逻辑）。</summary>
    private static readonly HashSet<string> DirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "dds", "png", "tga", "jpg", "jpeg", "webp",
    };

    // ---- 原始 BLP 字节解码（纯核心） ----------------------------------

    /// <summary>解码 BLP 字节为 Godot Image；失败返回 null。BLP1 4 通道 JPEG 也返回 null。</summary>
    public static Image? DecodeBytes(byte[] data)
    {
        RgbaImage? rgba = BlpDecoder.DecodeBytes(data);
        if (rgba != null)
            return ToGodotImage(rgba);
        return DecodeBlp1JpegFallback(data);
    }

    public static Image? decode_bytes(byte[] data) => DecodeBytes(data);

    /// <summary>BLP 是否声明 alpha。</summary>
    public static bool DeclaresAlpha(byte[] data) => BlpDecoder.DeclaresAlpha(data);

    public static bool declares_alpha(byte[] data) => DeclaresAlpha(data);

    /// <summary>把纯 C# RGBA 缓冲包装为 Godot Image。</summary>
    public static Image ToGodotImage(RgbaImage src) =>
        Image.CreateFromData(src.Width, src.Height, false, Image.Format.Rgba8, src.Rgba);

    /// <summary>BLP1 无 CMYK 4 通道时的原生 JPEG 解码兜底（RGB-only，无独立 alpha 平面）。</summary>
    private static Image? DecodeBlp1JpegFallback(byte[] data)
    {
        if (!BlpDecoder.TryExtractBlp1Jpeg(data, out int width, out int height, out byte[]? jpg) || jpg == null)
            return null;
        if (BlpDecoder.JpegComponentCount(jpg) == 4)
            return null; // 4 通道“假 CMYK”：交给离线 sidecar 转换工具
        var img = new Image();
        if (img.LoadJpgFromBuffer(jpg) != Error.Ok)
            return null;
        if (img.GetWidth() != width || img.GetHeight() != height)
            img.Resize(width, height);
        return img;
    }

    // ---- load_image：PNG/BLP/DDS sidecar 选择（迁移自 w3_blp_decoder.gd） --

    /// <summary>
    /// 按路径加载贴图：直读格式（dds/png/tga/jpg/webp）优先；否则在 BLP/PNG
    /// sidecar 间选择——BLP 声明 alpha 且 PNG 未保留透明时优先 BLP，
    /// 避免不透明 RGB sidecar（常见于树叶/火把）盖过带 alpha 的正确解码。
    /// </summary>
    public static Image? LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string directExt = GetExtension(path).ToLowerInvariant();
        if (DirectExtensions.Contains(directExt) && File.Exists(path))
        {
            if (directExt == "dds")
            {
                byte[] ddsBytes;
                try
                {
                    ddsBytes = File.ReadAllBytes(path);
                }
                catch
                {
                    return null;
                }
                var dds = new Image();
                return dds.LoadDdsFromBuffer(ddsBytes) == Error.Ok ? dds : null;
            }
            var direct = new Image();
            return direct.Load(path) == Error.Ok ? direct : null;
        }

        string basePath = GetBasename(path);
        string png = basePath + ".png";
        string blp = directExt == "blp" ? path : basePath + ".blp";

        Image? imgPng = null;
        if (File.Exists(png))
        {
            var candidate = new Image();
            imgPng = candidate.Load(png) == Error.Ok ? candidate : null;
        }

        Image? imgBlp = null;
        bool blpExists = File.Exists(blp);
        if (blpExists)
        {
            byte[]? blpData = TryReadAllBytes(blp);
            if (blpData != null)
            {
                // 未声明 alpha 的预转换 opaque sidecar 无损；优先于 Godot 无法
                // 直接解码的 War3 4 通道 JPEG（Windows 无 TurboJPEG）。
                if (imgPng != null && !BlpDecoder.DeclaresAlpha(blpData))
                    return imgPng;
                imgBlp = DecodeBytes(blpData);
            }
        }

        // BLP 保留透明时优先 BLP（树叶/火把），否则优先 PNG（地面更快）。
        if (imgPng != null && ImageHasTransparency(imgPng))
            return imgPng;
        if (imgBlp != null)
            return imgBlp;

        if (imgPng == null && blpExists)
        {
            byte[]? blpData = TryReadAllBytes(blp);
            if (blpData != null && BlpDecoder.IsCmykJpegBlp(blpData))
            {
                GD.PushError(
                    $"W3BlpDecoder: {blp} is a JPEG-CMYK BLP without a .png sidecar. " +
                    "Run: python tools/mpqtool/convert_cmyk_sidecars.py <asset_root>");
            }
        }
        return imgPng;
    }

    public static Image? load_image(string path) => LoadImage(path);

    /// <summary>取样网格判断是否存在非全不透明像素（避免全图扫描 512 图集的开销）。</summary>
    private static bool ImageHasTransparency(Image img)
    {
        if (img == null)
            return false;
        Image.Format fmt = img.GetFormat();
        // 不透明 RGB sidecar（常见于树叶）不能盖过 BLP。
        if (fmt == Image.Format.Rgb8 || fmt == Image.Format.Rgb565)
            return false;
        if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgba4444)
            img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth();
        int h = img.GetHeight();
        if (w <= 0 || h <= 0)
            return false;
        int stepX = Math.Max(1, w / 32);
        int stepY = Math.Max(1, h / 32);
        for (int y = 0; y < h; y += stepY)
        {
            for (int x = 0; x < w; x += stepX)
            {
                if (img.GetPixel(x, y).A < 0.98f)
                    return true;
            }
        }
        return false;
    }

    /// <summary>GDS 私有命名别名：回归测试直接调用（tools/w3_regression.gd）。</summary>
    public static bool _valid_dimensions(int width, int height) => BlpDecoder.ValidDimensions(width, height);

    /// <summary>GDS 私有命名别名：调试工具直接调用（tools/w3_water_frames_dump.gd）。</summary>
    public static int _jpeg_component_count(byte[] jpg) => BlpDecoder.JpegComponentCount(jpg);

    private static byte[]? TryReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Godot get_extension() 等价：取最后一个路径分隔符之后的扩展名。</summary>
    private static string GetExtension(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int dot = path.LastIndexOf('.');
        return dot > slash ? path[(dot + 1)..] : "";
    }

    /// <summary>Godot get_basename() 等价：去掉最后一个扩展名，保留目录部分。</summary>
    private static string GetBasename(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int dot = path.LastIndexOf('.');
        return dot > slash ? path[..dot] : path;
    }
}
