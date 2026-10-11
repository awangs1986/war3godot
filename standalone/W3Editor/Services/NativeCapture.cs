using System.Runtime.InteropServices;

namespace W3Editor.Services;

/// <summary>
/// Screenshot helper that also captures native swapchain surfaces (the GL
/// viewport), which Avalonia's RenderTargetBitmap cannot always see.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class NativeCapture
{
    /// <summary>PrintWindow(PW_RENDERFULLCONTENT) of a top-level window into a PNG.</summary>
    public static bool CaptureWindow(IntPtr hwnd, string pngPath)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc))
            return false;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0)
            return false;
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok)
                return false;
        }
        bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
        return true;
    }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
