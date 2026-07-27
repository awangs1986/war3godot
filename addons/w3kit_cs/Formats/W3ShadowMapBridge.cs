using Godot;
using W3Compat.Formats;

namespace Diablo1Like.W3.Formats;

/// <summary>将 W3Compat SHD 打包结果转为 Godot ImageTexture。</summary>
[GlobalClass]
public partial class W3ShadowMapBridge : RefCounted
{
    /// <summary>期望 SHD 字节数。</summary>
    public int ExpectedSize(int tilesX, int tilesY) => W3ShadowMap.ExpectedSize(tilesX, tilesY);

    public int expected_size(int tilesX, int tilesY) => ExpectedSize(tilesX, tilesY);

    /// <summary>加载 R8 阴影纹理；尺寸不符返回 null。</summary>
    public ImageTexture? LoadTexture(byte[] bytes, int tilesX, int tilesY)
    {
        byte[]? packed = W3ShadowMap.PackR8Half(bytes, tilesX, tilesY);
        if (packed == null)
        {
            if (bytes.Length > 0 && tilesX > 0 && tilesY > 0)
            {
                int need = W3ShadowMap.ExpectedSize(tilesX, tilesY);
                GD.PushWarning($"W3Shd: size {bytes.Length} < expected {need} ({tilesX * 4}x{tilesY * 4})");
            }

            return null;
        }

        int w = W3ShadowMap.PixelWidth(tilesX);
        int h = W3ShadowMap.PixelHeight(tilesY);
        Image img = Image.CreateFromData(w, h, false, Image.Format.R8, packed);
        return ImageTexture.CreateFromImage(img);
    }

    public ImageTexture? load_texture(byte[] bytes, int tilesX, int tilesY) =>
        LoadTexture(bytes, tilesX, tilesY);

    /// <summary>全黑 4x4 R8 占位纹理。</summary>
    public ImageTexture BlankTexture()
    {
        Image img = Image.CreateEmpty(4, 4, false, Image.Format.R8);
        img.Fill(Colors.Black);
        return ImageTexture.CreateFromImage(img);
    }

    public ImageTexture blank_texture() => BlankTexture();
}
