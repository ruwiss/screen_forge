using WpfRect = System.Windows.Rect;

namespace ScreenForge.Windows;

/// <summary>
/// Çeviri sonuç görseli overlay'in sanal masaüstünü kapladığı için
/// ActualWidth/Height ile ortalanırsa iki monitörün arasına düşer.
/// Tüm DIP hesapları hedef monitör dikdörtgenine göredir.
/// </summary>
internal static class TranslateResultLayout
{
    internal readonly record struct HostLayout(double Left, double Top, double Width, double Height);
    internal readonly record struct ChromeLayout(double CloseLeft, double CloseTop, double CopyLeft, double CopyTop);
    internal readonly record struct ZoomLayout(double Scale, double Left, double Top);

    public static HostLayout Host(WpfRect monitor, double imgW, double imgH, double selW, double selH)
    {
        double screenW = Math.Max(1, monitor.Width);
        double screenH = Math.Max(1, monitor.Height);
        imgW = Math.Max(1, imgW);
        imgH = Math.Max(1, imgH);
        double imgAspect = imgW / imgH;

        double hostW;
        double hostH;
        if (selW > 2 && selH > 2)
        {
            double s = Math.Min(selW / imgW, selH / imgH);
            hostW = imgW * s * 1.08;
            hostH = imgH * s * 1.08;
        }
        else
        {
            hostW = imgW;
            hostH = imgH;
        }

        double minH = Math.Max(72, screenH * 0.14);
        if (hostH < minH && hostH > 0.5)
        {
            double f = minH / hostH;
            if (selH > 2)
                f = Math.Min(f, 1.45);
            hostW *= f;
            hostH *= f;
        }

        double maxW = screenW * 0.88;
        double maxH = screenH * 0.75;
        if (hostW > maxW || hostH > maxH)
        {
            double f = Math.Min(maxW / hostW, maxH / hostH);
            hostW *= f;
            hostH *= f;
        }

        if (Math.Abs(hostW / hostH - imgAspect) > 0.01)
        {
            if (hostW / hostH > imgAspect) hostW = hostH * imgAspect;
            else hostH = hostW / imgAspect;
        }

        hostW = Math.Max(1, hostW);
        hostH = Math.Max(1, hostH);
        return new HostLayout(
            monitor.Left + (screenW - hostW) / 2,
            monitor.Top + (screenH - hostH) / 2,
            hostW,
            hostH);
    }

    public static ChromeLayout Chrome(WpfRect monitor, double copyButtonWidth)
    {
        double copyW = Math.Max(1, copyButtonWidth);
        return new ChromeLayout(
            monitor.Right - 40 - 20,
            monitor.Top + 20,
            monitor.Right - copyW - 24,
            monitor.Bottom - 40 - 24);
    }

    public static ZoomLayout Zoom(WpfRect monitor, double hostW, double hostH, bool zoomed, double margin = 20)
    {
        hostW = Math.Max(1, hostW);
        hostH = Math.Max(1, hostH);

        double s = 1.0;
        if (zoomed)
        {
            double maxScale = Math.Min(
                (monitor.Width - 2 * margin) / hostW,
                (monitor.Height - 2 * margin) / hostH);
            s = Math.Min(1.35, Math.Max(1.0, maxScale));
            if (s < 1.05) s = 1.0;
        }

        double visW = hostW * s;
        double visH = hostH * s;
        double left = monitor.Left + (monitor.Width - visW) / 2;
        double top = monitor.Top + (monitor.Height - visH) / 2;
        left = Math.Clamp(left, monitor.Left + margin, Math.Max(monitor.Left + margin, monitor.Right - visW - margin));
        top = Math.Clamp(top, monitor.Top + margin, Math.Max(monitor.Top + margin, monitor.Bottom - visH - margin));

        // ScaleTransform origin 0.5,0.5: Canvas sol-üst unscaled kalır
        return new ZoomLayout(s, left + (visW - hostW) / 2, top + (visH - hostH) / 2);
    }
}
