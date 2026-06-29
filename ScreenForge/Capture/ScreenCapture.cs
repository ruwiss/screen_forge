using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ScreenForge.Capture;

/// <summary>
/// Ham ekran yakalama. Tüm monitörleri kapsayan "sanal ekran"ı fiziksel piksellerde
/// BitBlt ile alır. Süreç PerMonitorV2 DPI farkında olduğundan Win32 metrikleri
/// fiziksel pikseldir.
/// </summary>
public static class ScreenCapture
{
    // ---- Win32 ----
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
    private const int DI_NORMAL = 0x0003;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const int CURSOR_SHOWING = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    /// <summary>Sanal ekranın fiziksel piksel sınırları (tüm monitörler birleşik).</summary>
    public static Rectangle VirtualScreenBounds => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>Tüm ekranların birleşik görüntüsünü yakalar.</summary>
    public static Bitmap CaptureVirtualScreen(bool includeCursor)
    {
        var bounds = VirtualScreenBounds;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr desktop = GetDesktopWindow();
            IntPtr hdcSrc = GetWindowDC(desktop);
            IntPtr hdcDest = g.GetHdc();
            try
            {
                BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height, hdcSrc, bounds.X, bounds.Y, SRCCOPY);
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
                ReleaseDC(desktop, hdcSrc);
            }

            if (includeCursor)
                DrawCursor(g, bounds);
        }
        return bmp;
    }

    private static void DrawCursor(Graphics g, Rectangle bounds)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING) return;

        IntPtr hIcon = CopyIcon(ci.hCursor);
        if (hIcon == IntPtr.Zero) return;
        try
        {
            if (!GetIconInfo(hIcon, out var icon)) return;
            int x = ci.ptScreenPos.x - bounds.X - icon.xHotspot;
            int y = ci.ptScreenPos.y - bounds.Y - icon.yHotspot;
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);

            IntPtr hdc = g.GetHdc();
            try { DrawIconEx(hdc, x, y, hIcon, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
        catch { /* imleç çizilemezse yut */ }
        finally { DestroyIcon(hIcon); }
    }

    /// <summary>Verilen fiziksel-piksel dikdörtgenini sanal ekran bitmap'inden kırpar.</summary>
    public static Bitmap Crop(Bitmap source, Rectangle region)
    {
        region.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (region.Width <= 0 || region.Height <= 0)
            region = new Rectangle(0, 0, 1, 1);
        var dst = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
        return dst;
    }

    /// <summary>System.Drawing.Bitmap → WPF BitmapSource (UI'da göstermek için).</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bs = BitmapSource.Create(
                bmp.Width, bmp.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            bs.Freeze();
            return bs;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
