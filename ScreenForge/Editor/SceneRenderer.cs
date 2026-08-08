using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Sahneyi bir SKCanvas'a çizer. Blur içerikleri paint sırasında üretilmez;
/// <see cref="BakeDirtyBlurs"/> ile ayrı, kontrollü anda pişirilir.
/// </summary>
public static class SceneRenderer
{
    public static bool HighQualityPass { get; private set; }

    private static readonly SKSamplingOptions FastSampling = new(SKFilterMode.Linear);
    private static readonly SKSamplingOptions QualitySampling = new(SKCubicResampler.Mitchell);

    public static SKBitmap RenderToBitmap(Scene scene)
    {
        // Export öncesi dirty blur'ları pişir.
        BakeDirtyBlurs(scene);

        int w = Math.Max(1, (int)Math.Round(scene.Width));
        int h = Math.Max(1, (int)Math.Round(scene.Height));
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.ClipRect(new SKRect(0, 0, w, h));
        RenderContent(canvas, scene, highQuality: true);
        return bmp;
    }

    /// <summary>
    /// Arka plan + öğeleri çizer. Paint yolunda blur capture/allocate YOK.
    /// </summary>
    public static void RenderContent(SKCanvas canvas, Scene scene, bool highQuality = false, bool skipBlurSnapshot = false)
    {
        // skipBlurSnapshot parametresi geriye dönük uyumluluk için duruyor; artık kullanılmıyor.
        _ = skipBlurSnapshot;

        bool prevHq = HighQualityPass;
        HighQualityPass = highQuality;
        try
        {
            if (scene.BackgroundColor.Alpha > 0)
                canvas.Clear(scene.BackgroundColor);

            var bg = scene.Background;
            var bgImg = scene.GetBackgroundImage();
            if (bgImg != null && bg != null)
            {
                var sampling = highQuality ? QualitySampling : FastSampling;
                canvas.DrawImage(bgImg, new SKRect(0, 0, bg.Width, bg.Height), sampling);
            }

            foreach (var item in scene.Items)
            {
                if (scene.HitFilter != null && !scene.HitFilter(item))
                    continue;
                item.Render(canvas);
            }
        }
        finally
        {
            HighQualityPass = prevHq;
        }
    }

    /// <summary>
    /// NeedsBake olan tüm BlurItem'ları pişirir. Sürükleme/resize sırasında ÇAĞRILMAZ;
    /// yalnızca commit / stil değişimi / export.
    /// </summary>
    public static void BakeDirtyBlurs(Scene scene)
    {
        bool any = false;
        foreach (var item in scene.Items)
        {
            if (item is BlurItem { NeedsBake: true }) { any = true; break; }
        }
        if (!any) return;

        var bg = scene.Background;
        var bgImg = scene.GetBackgroundImage();
        int sceneW = Math.Max(1, (int)Math.Round(scene.Width));
        int sceneH = Math.Max(1, (int)Math.Round(scene.Height));

        foreach (var item in scene.Items)
        {
            if (item is not BlurItem blur || !blur.NeedsBake)
                continue;

            try
            {
                var baked = CaptureAndBlur(scene, blur, bgImg, bg, sceneW, sceneH);
                blur.SetBaked(baked);
            }
            catch
            {
                blur.SetBaked(null);
            }
        }
    }

    private static SKBitmap? CaptureAndBlur(
        Scene scene, BlurItem blur, SKImage? bgImg, SKBitmap? bg, int sceneW, int sceneH)
    {
        var b = blur.Bounds;
        if (b.Width < 1f || b.Height < 1f) return null;

        int left = Math.Clamp((int)Math.Floor(b.Left), 0, sceneW);
        int top = Math.Clamp((int)Math.Floor(b.Top), 0, sceneH);
        int right = Math.Clamp((int)Math.Ceiling(b.Right), 0, sceneW);
        int bottom = Math.Clamp((int)Math.Ceiling(b.Bottom), 0, sceneH);
        int rw = right - left;
        int rh = bottom - top;
        if (rw < 1 || rh < 1) return null;

        // Küçük ara buffer — bellek ve süre güvenli.
        const int maxSide = 256;
        float scale = 1f;
        int capW = rw, capH = rh;
        if (capW > maxSide || capH > maxSide)
        {
            scale = Math.Min((float)maxSide / capW, (float)maxSide / capH);
            capW = Math.Max(1, (int)Math.Round(rw * scale));
            capH = Math.Max(1, (int)Math.Round(rh * scale));
        }

        using var region = new SKBitmap(capW, capH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(region))
        {
            c.Clear(SKColors.Transparent);
            if (scene.BackgroundColor.Alpha > 0)
                c.Clear(scene.BackgroundColor);

            c.Scale(scale);
            c.Translate(-left, -top);

            if (bgImg != null && bg != null)
                c.DrawImage(bgImg, new SKRect(0, 0, bg.Width, bg.Height), FastSampling);

            foreach (var it in scene.Items)
            {
                if (ReferenceEquals(it, blur)) break;
                if (it is BlurItem) continue; // pişmemiş/diğer blur'ları atla
                it.Render(c);
            }
        }

        float strength = Math.Clamp(blur.Strength, 1f, 64f);

        if (blur.Pixelate)
        {
            int tw = Math.Max(1, (int)(capW / strength));
            int th = Math.Max(1, (int)(capH / strength));
            using var tiny = region.Resize(new SKImageInfo(tw, th), SKSamplingOptions.Default);
            if (tiny == null) return region.Copy();
            // Bounds boyutuna geri büyüt (Nearest = piksel görünümü)
            int outW = Math.Max(1, Math.Min(rw, 512));
            int outH = Math.Max(1, Math.Min(rh, 512));
            return tiny.Resize(new SKImageInfo(outW, outH), new SKSamplingOptions(SKFilterMode.Nearest));
        }

        // Soft blur: downscale → upscale (CreateBlur kullanma — native crash riski).
        float factor = Math.Max(1.5f, strength * 0.85f);
        int dw = Math.Max(1, (int)Math.Ceiling(capW / factor));
        int dh = Math.Max(1, (int)Math.Ceiling(capH / factor));
        const int maxDim = 64;
        if (dw > maxDim || dh > maxDim)
        {
            float s = Math.Min((float)maxDim / dw, (float)maxDim / dh);
            dw = Math.Max(1, (int)(dw * s));
            dh = Math.Max(1, (int)(dh * s));
        }

        using var small = region.Resize(new SKImageInfo(dw, dh), new SKSamplingOptions(SKFilterMode.Linear));
        if (small == null) return region.Copy();

        int ow = Math.Max(1, Math.Min(rw, 512));
        int oh = Math.Max(1, Math.Min(rh, 512));
        return small.Resize(new SKImageInfo(ow, oh), new SKSamplingOptions(SKFilterMode.Linear));
    }
}
