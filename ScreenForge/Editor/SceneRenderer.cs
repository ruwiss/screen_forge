using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Sahneyi bir SKCanvas'a çizer. İki mod: ekran (seçim tutamaçları dahil) ve
/// export (sadece içerik). Blur öğeleri için altındaki katmanın anlık görüntüsünü üretir.
/// </summary>
public static class SceneRenderer
{
    private static SKImage? _bgCache;
    private static SKBitmap? _bgCacheSource;

    /// <summary>Sahneyi export için bir bitmap'e çizer (seçim göstergeleri olmadan).</summary>
    public static SKBitmap RenderToBitmap(Scene scene)
    {
        int w = Math.Max(1, (int)Math.Round(scene.Width));
        int h = Math.Max(1, (int)Math.Round(scene.Height));
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        RenderContent(canvas, scene, highQuality: true);
        return bmp;
    }

    private static SKImage? GetBackgroundImage(SKBitmap? bg)
    {
        if (bg == null) { _bgCache?.Dispose(); _bgCache = null; _bgCacheSource = null; return null; }
        if (bg == _bgCacheSource && _bgCache != null) return _bgCache;
        _bgCache?.Dispose();
        _bgCache = SKImage.FromBitmap(bg);
        _bgCacheSource = bg;
        return _bgCache;
    }

    private static readonly SKSamplingOptions FastSampling = new(SKFilterMode.Linear);
    private static readonly SKSamplingOptions QualitySampling = new(SKCubicResampler.Mitchell);

    /// <summary>Arka plan + tüm öğeleri çizer (seçim hariç).</summary>
    /// <param name="skipBlurSnapshot">
    /// true = blur snapshot yeniden hesaplanmaz (sürükleme sırasında; blur görsel
    /// önceki snapshot ile kalır, interaktivite için yeterli).
    /// </param>
    public static void RenderContent(SKCanvas canvas, Scene scene, bool highQuality = false, bool skipBlurSnapshot = false)
    {
        if (scene.BackgroundColor.Alpha > 0)
            canvas.Clear(scene.BackgroundColor);

        var bgImg = GetBackgroundImage(scene.Background);
        if (bgImg != null && scene.Background != null)
        {
            var sampling = highQuality ? QualitySampling : FastSampling;
            canvas.DrawImage(bgImg, new SKRect(0, 0, scene.Background.Width, scene.Background.Height), sampling);
        }

        if (!skipBlurSnapshot)
        {
            bool hasBlur = false;
            foreach (var item in scene.Items) { if (item is BlurItem) { hasBlur = true; break; } }
            if (hasBlur) PrepareBlurSnapshots(scene, bgImg);
        }

        foreach (var item in scene.Items)
        {
            using var layer = item.Opacity < 1f
                ? new AutoLayer(canvas, item.Opacity)
                : null;
            item.Render(canvas);
        }
    }

    private static void PrepareBlurSnapshots(Scene scene, SKImage? bgImg)
    {
        int w = Math.Max(1, (int)Math.Round(scene.Width));
        int h = Math.Max(1, (int)Math.Round(scene.Height));

        foreach (var item in scene.Items)
        {
            if (item is not BlurItem blur) continue;

            var snap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(snap))
            {
                c.Clear(SKColors.Transparent);
                if (scene.BackgroundColor.Alpha > 0) c.Clear(scene.BackgroundColor);
                if (bgImg != null && scene.Background != null)
                    c.DrawImage(bgImg, new SKRect(0, 0, scene.Background.Width, scene.Background.Height), FastSampling);

                foreach (var it in scene.Items)
                {
                    if (it == blur) break;
                    if (it is BlurItem) continue;
                    it.Render(c);
                }
            }
            blur.SourceSnapshot?.Dispose();
            blur.SourceSnapshot = snap;
        }
    }
}

/// <summary>Opacity layer'ı RAII tarzı yöneten yardımcı.</summary>
internal sealed class AutoLayer : IDisposable
{
    private readonly SKCanvas _canvas;
    public AutoLayer(SKCanvas canvas, float opacity)
    {
        _canvas = canvas;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(opacity * 255)) };
        _canvas.SaveLayer(paint);
    }
    public void Dispose() => _canvas.Restore();
}
