using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace ScreenForge.Windows;

/// <summary>
/// Overlay tek HWND ile sanal ekranı kaplar; WPF pencereye tek DPI verir.
/// Chrome ölçeği = toolbar'ın durduğu monitörün etkili DPI'si / pencere DPI'si.
/// Oran ~1 ise ekstra zoom yok; yakalama koordinatları değişmez.
/// </summary>
internal static class ChromeScale
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    /// <summary>Monitör etkili DPI / pencere DPI. Başarısızsa veya oran ~1 ise 1.</summary>
    public static double ForScreenPoint(Visual visual, int screenX, int screenY)
    {
        var windowDpi = VisualTreeHelper.GetDpi(visual);
        double windowPx = windowDpi.PixelsPerInchX;
        if (windowPx <= 0)
            windowPx = 96;

        var hmon = MonitorFromPoint(new POINT { X = screenX, Y = screenY }, MonitorDefaultToNearest);
        if (hmon == IntPtr.Zero)
            return 1;
        if (GetDpiForMonitor(hmon, MdtEffectiveDpi, out uint dpiX, out _) != 0 || dpiX == 0)
            return 1;

        double scale = dpiX / windowPx;
        return Math.Abs(scale - 1) < 0.01 ? 1 : scale;
    }

    public static void Apply(FrameworkElement element, double scale)
    {
        if (scale <= 0 || Math.Abs(scale - 1) < 0.01)
        {
            if (element.LayoutTransform != null && !ReferenceEquals(element.LayoutTransform, Transform.Identity))
                element.LayoutTransform = Transform.Identity;
            return;
        }

        if (element.LayoutTransform is ScaleTransform existing
            && Math.Abs(existing.ScaleX - scale) < 0.001
            && Math.Abs(existing.ScaleY - scale) < 0.001)
            return;

        element.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>LayoutTransform dahil görünen boyut (Canvas yerleşimi için).</summary>
    public static Size LayoutSize(FrameworkElement el)
    {
        double w = el.ActualWidth;
        double h = el.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            el.UpdateLayout();
            w = el.ActualWidth;
            h = el.ActualHeight;
        }

        if (el.LayoutTransform is { } t && !ReferenceEquals(t, Transform.Identity))
        {
            var bounds = t.TransformBounds(new Rect(0, 0, w, h));
            return new Size(bounds.Width, bounds.Height);
        }

        return new Size(w, h);
    }
}
