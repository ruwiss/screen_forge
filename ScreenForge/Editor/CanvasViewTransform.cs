using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// SKElement yüzey pikselleri ↔ sahne koordinatı. Paint ve fare aynı formülü kullanır;
/// DpiScale * stale _scale eşlemesi yüksek DPI'da kırpmayı ekranın ortasında bitiriyordu.
/// </summary>
internal static class CanvasViewTransform
{
    public static (float Scale, SKPoint Offset) Compute(
        LayoutMode layout, float sceneW, float sceneH, int pxW, int pxH, float dpi)
    {
        if (sceneW <= 0 || sceneH <= 0)
            return (1f, new SKPoint(0, 0));

        if (layout == LayoutMode.OneToOne)
        {
            float s = pxW > 0 ? pxW / sceneW : 1f;
            if (s <= 0 || float.IsNaN(s)) s = dpi > 0 ? dpi : 1f;
            return (s, new SKPoint(0, 0));
        }

        float margin = 24f * Math.Max(dpi, 1f);
        float availW = pxW - margin * 2, availH = pxH - margin * 2;
        float scale = Math.Min(availW / sceneW, availH / sceneH);
        scale = Math.Min(scale, 4f);
        if (scale <= 0 || float.IsNaN(scale)) scale = 1f;
        float ox = (pxW - sceneW * scale) / 2f;
        float oy = (pxH - sceneH * scale) / 2f;
        return (scale, new SKPoint(ox, oy));
    }

    public static SKPoint WpfToScene(
        double x, double y, double actualW, double actualH,
        int paintPxW, int paintPxH, float scale, SKPoint offset)
    {
        if (actualW <= 0 || actualH <= 0 || paintPxW <= 0 || paintPxH <= 0 || scale <= 0)
            return new SKPoint((float)x, (float)y);

        float px = (float)(x * paintPxW / actualW);
        float py = (float)(y * paintPxH / actualH);
        return new SKPoint((px - offset.X) / scale, (py - offset.Y) / scale);
    }

    public static (double X, double Y) SceneToWpf(
        float sceneX, float sceneY, double actualW, double actualH,
        int paintPxW, int paintPxH, float scale, SKPoint offset)
    {
        if (actualW <= 0 || actualH <= 0 || paintPxW <= 0 || paintPxH <= 0)
            return (sceneX, sceneY);

        float px = sceneX * scale + offset.X;
        float py = sceneY * scale + offset.Y;
        return (px * actualW / paintPxW, py * actualH / paintPxH);
    }
}
