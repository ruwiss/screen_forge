using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.Brush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectF = System.Drawing.RectangleF;
using DrawingSolidBrush = System.Drawing.SolidBrush;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Altyazı, ilerleme göstergesi, kenarlık ve filigranı karelere çizer.
/// </summary>
/// <remarks>
/// Kaplamalar karelere kalıcı yazılmaz; yalnızca dışa aktarım sırasında
/// uygulanır. Böylece ayar sonradan değiştirilebilir ve geri alma gerekmez.
/// </remarks>
internal static class OverlayRenderer
{
    /// <summary>
    /// Kareyi kopyalayıp üzerine kaplamaları çizer.
    /// Çizilecek bir şey yoksa özgün dizi döner.
    /// </summary>
    /// <param name="bgra">BGRA kare verisi.</param>
    /// <param name="width">Kare genişliği.</param>
    /// <param name="height">Kare yüksekliği.</param>
    /// <param name="set">Uygulanacak kaplamalar.</param>
    /// <param name="frameIndex">Karenin dizideki sırası (ilerleme için).</param>
    /// <param name="frameCount">Toplam kare sayısı.</param>
    /// <param name="elapsedMs">Bu kareye kadar geçen süre.</param>
    /// <param name="totalMs">Animasyonun toplam süresi.</param>
    /// <param name="scale">Kaynak boyuttan çıktı boyutuna ölçek.</param>
    public static byte[] Apply(byte[] bgra, int width, int height, OverlaySet set,
        int frameIndex, int frameCount, long elapsedMs, long totalMs, double scale = 1.0)
    {
        if (!set.HasWork || width <= 0 || height <= 0)
            return bgra;

        var output = new byte[bgra.Length];
        Buffer.BlockCopy(bgra, 0, output, 0, bgra.Length);

        var handle = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            using var bitmap = new DrawingBitmap(width, height, width * 4,
                DrawingPixelFormat.Format32bppArgb, handle.AddrOfPinnedObject());
            using var graphics = DrawingGraphics.FromImage(bitmap);

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (set.Border.HasWork)
                DrawBorder(graphics, width, height, set.Border, scale);

            if (set.Progress.HasWork)
                DrawProgress(graphics, width, height, set.Progress, frameIndex, frameCount, elapsedMs, totalMs, scale);

            if (set.Caption.HasWork)
                DrawCaption(graphics, width, height, set.Caption, scale);

            if (set.Watermark.HasWork)
                DrawWatermark(graphics, width, height, set.Watermark, scale);
        }
        finally
        {
            handle.Free();
        }

        // GDI+ alfayı bozabilir; GIF yolunda tüm pikseller opak olmalı.
        for (int i = 3; i < output.Length; i += 4) output[i] = 255;
        return output;
    }

    // ─── Kenarlık ─────────────────────────────────────────────────────────────

    private static void DrawBorder(DrawingGraphics graphics, int width, int height, BorderOptions options, double scale)
    {
        float thickness = (float)Math.Max(1, options.Thickness * scale);

        // İçe doğru çiz: kare boyutu değişmesin.
        using var pen = new System.Drawing.Pen(options.Color, thickness);
        pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
        graphics.DrawRectangle(pen, thickness / 2, thickness / 2, width - thickness, height - thickness);
    }

    // ─── İlerleme ─────────────────────────────────────────────────────────────

    private static void DrawProgress(DrawingGraphics graphics, int width, int height, ProgressOptions options,
        int frameIndex, int frameCount, long elapsedMs, long totalMs, double scale)
    {
        double fraction = frameCount <= 1 ? 1.0 : Math.Clamp((frameIndex + 1) / (double)frameCount, 0, 1);

        if (options.Style == ProgressStyle.Text)
        {
            DrawProgressText(graphics, width, height, options, frameIndex, frameCount, elapsedMs, totalMs, fraction, scale);
            return;
        }

        float thickness = (float)Math.Max(1, options.Thickness * scale);

        if (options.Vertical)
        {
            bool right = options.Placement is OverlayPlacement.TopRight
                or OverlayPlacement.MiddleRight or OverlayPlacement.BottomRight;
            float x = right ? width - thickness : 0;

            if (options.TrackColor.A > 0)
                using (var track = new DrawingSolidBrush(options.TrackColor))
                    graphics.FillRectangle(track, x, 0, thickness, height);

            float filled = (float)(height * fraction);
            using var brush = new DrawingSolidBrush(options.Color);
            graphics.FillRectangle(brush, x, height - filled, thickness, filled);
            return;
        }

        bool bottom = options.Placement is OverlayPlacement.BottomLeft
            or OverlayPlacement.BottomCenter or OverlayPlacement.BottomRight;
        float y = bottom ? height - thickness : 0;

        if (options.TrackColor.A > 0)
            using (var track = new DrawingSolidBrush(options.TrackColor))
                graphics.FillRectangle(track, 0, y, width, thickness);

        using var barBrush = new DrawingSolidBrush(options.Color);
        graphics.FillRectangle(barBrush, 0, y, (float)(width * fraction), thickness);
    }

    private static void DrawProgressText(DrawingGraphics graphics, int width, int height, ProgressOptions options,
        int frameIndex, int frameCount, long elapsedMs, long totalMs, double fraction, double scale)
    {
        string text = FormatReadout(options, frameIndex, frameCount, elapsedMs, totalMs, fraction);

        DrawLabel(graphics, width, height, text, options.FontFamily, options.FontSize, bold: true,
            options.TextColor, options.TextBackgroundColor, options.Placement, options.Margin, scale);
    }

    /// <summary>
    /// İlerleme yazısını biçimlendirir.
    /// </summary>
    /// <remarks>
    /// Saniye gösteriminde ondalık basamak sayısı ayarlanabilir: kısa
    /// kayıtlarda salise anlamlıyken uzun kayıtlarda tam sayı daha okunaklıdır.
    /// </remarks>
    public static string FormatReadout(ProgressOptions options,
        int frameIndex, int frameCount, long elapsedMs, long totalMs, double fraction)
    {
        if (options.Readout == ProgressReadout.Frames)
            return $"{frameIndex + 1}/{frameCount}";

        if (options.Readout == ProgressReadout.Percent)
            return $"{fraction * 100:0}%";

        string format = options.SecondsDecimals <= 0 ? "0" : "0." + new string('0', Math.Min(options.SecondsDecimals, 3));
        return $"{(elapsedMs / 1000.0).ToString(format)}/{(totalMs / 1000.0).ToString(format)} sn";
    }

    // ─── Metin kaplamaları ────────────────────────────────────────────────────

    private static void DrawCaption(DrawingGraphics graphics, int width, int height, CaptionOptions options, double scale)
    {
        float fontSize = (float)Math.Max(6, options.FontSize * scale);
        using var font = new DrawingFont(options.FontFamily,
            fontSize, options.Bold ? DrawingFontStyle.Bold : DrawingFontStyle.Regular,
            System.Drawing.GraphicsUnit.Pixel);

        var size = graphics.MeasureString(options.Text, font, width);
        float padX = fontSize * 0.6f;
        float padY = fontSize * 0.35f;
        float margin = (float)(options.Margin * scale);

        var box = PlaceBox(width, height, size.Width + padX * 2, size.Height + padY * 2, options.Placement, margin);

        if (options.BackgroundColor.A > 0)
            using (var background = new DrawingSolidBrush(options.BackgroundColor))
                FillRounded(graphics, background, box, fontSize * 0.3f);

        var textRect = new DrawingRectF(box.X + padX, box.Y + padY, size.Width, size.Height);

        if (options.OutlineThickness > 0 && options.OutlineColor.A > 0)
        {
            DrawOutlinedText(graphics, options.Text, font, textRect,
                options.Color, options.OutlineColor, (float)(options.OutlineThickness * scale));
            return;
        }

        using var brush = new DrawingSolidBrush(options.Color);
        graphics.DrawString(options.Text, font, brush, textRect);
    }

    private static void DrawWatermark(DrawingGraphics graphics, int width, int height, WatermarkOptions options, double scale)
    {
        if (options.HasImage)
        {
            DrawWatermarkImage(graphics, width, height, options, scale);
            return;
        }

        DrawLabel(graphics, width, height, options.Text, options.FontFamily, options.FontSize, options.Bold,
            options.Color, DrawingColor.Transparent, options.Placement, options.Margin, scale);
    }

    /// <summary>
    /// Logo dosyasını kareye yerleştirir.
    /// </summary>
    /// <remarks>
    /// Görsel kare genişliğinin oranı olarak ölçeklenir; böylece farklı
    /// çıktı boyutlarında aynı görsel ağırlıkta kalır. En-boy oranı korunur.
    /// </remarks>
    private static void DrawWatermarkImage(DrawingGraphics graphics, int width, int height,
        WatermarkOptions options, double scale)
    {
        System.Drawing.Image image;

        try
        {
            image = LoadImage(options.ImagePath!);
        }
        catch (Exception)
        {
            // Dosya silinmiş ya da bozuksa filigran atlanır; dışa aktarım sürer.
            return;
        }

        double target = width * Math.Clamp(options.ImageScale, 0.02, 0.5);
        double ratio = image.Height / (double)image.Width;

        float drawWidth = (float)target;
        float drawHeight = (float)(target * ratio);
        float margin = (float)(options.Margin * scale);

        var box = PlaceBox(width, height, drawWidth, drawHeight, options.Placement, margin);

        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix33 = (float)Math.Clamp(options.ImageOpacity, 0, 1),
        };

        attributes.SetColorMatrix(matrix);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image,
            new System.Drawing.Rectangle((int)box.X, (int)box.Y, (int)drawWidth, (int)drawHeight),
            0, 0, image.Width, image.Height, System.Drawing.GraphicsUnit.Pixel, attributes);
    }

    // Aynı logo her karede yeniden okunmasın; dosya yolu başına önbelleklenir.
    private static readonly Dictionary<string, System.Drawing.Image> ImageCache = new();
    private static readonly object ImageCacheGate = new();

    private static System.Drawing.Image LoadImage(string path)
    {
        lock (ImageCacheGate)
        {
            if (ImageCache.TryGetValue(path, out var cached))
                return cached;

            // Dosya kilidi bırakılsın diye bellekten yüklenir.
            using var stream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path));
            var image = System.Drawing.Image.FromStream(stream);

            ImageCache[path] = image;
            return image;
        }
    }

    private static void DrawLabel(DrawingGraphics graphics, int width, int height, string text,
        string fontFamily, double fontSize, bool bold, DrawingColor color, DrawingColor background,
        OverlayPlacement placement, double margin, double scale)
    {
        float size = (float)Math.Max(6, fontSize * scale);
        using var font = new DrawingFont(fontFamily, size,
            bold ? DrawingFontStyle.Bold : DrawingFontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);

        var measured = graphics.MeasureString(text, font);
        float padX = size * 0.5f;
        float padY = size * 0.3f;
        float m = (float)(margin * scale);

        var box = PlaceBox(width, height, measured.Width + padX * 2, measured.Height + padY * 2, placement, m);

        if (background.A > 0)
            using (var brush = new DrawingSolidBrush(background))
                FillRounded(graphics, brush, box, size * 0.3f);

        using var foreground = new DrawingSolidBrush(color);
        graphics.DrawString(text, font, foreground, new DrawingPointF(box.X + padX, box.Y + padY));
    }

    private static void DrawOutlinedText(DrawingGraphics graphics, string text, DrawingFont font,
        DrawingRectF rect, DrawingColor fill, DrawingColor outline, float thickness)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        using var format = new System.Drawing.StringFormat();

        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, rect, format);

        using (var pen = new System.Drawing.Pen(outline, Math.Max(0.5f, thickness)))
        {
            pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
            graphics.DrawPath(pen, path);
        }

        using var brush = new DrawingSolidBrush(fill);
        graphics.FillPath(brush, path);
    }

    // ─── Yerleşim ─────────────────────────────────────────────────────────────

    internal static DrawingRectF PlaceBox(int width, int height, float boxWidth, float boxHeight,
        OverlayPlacement placement, float margin)
    {
        float x = placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.MiddleLeft or OverlayPlacement.BottomLeft => margin,
            OverlayPlacement.TopRight or OverlayPlacement.MiddleRight or OverlayPlacement.BottomRight => width - boxWidth - margin,
            _ => (width - boxWidth) / 2,
        };

        float y = placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.TopCenter or OverlayPlacement.TopRight => margin,
            OverlayPlacement.MiddleLeft or OverlayPlacement.MiddleCenter or OverlayPlacement.MiddleRight => (height - boxHeight) / 2,
            _ => height - boxHeight - margin,
        };

        // Kare dışına taşmasın.
        x = Math.Clamp(x, 0, Math.Max(0, width - boxWidth));
        y = Math.Clamp(y, 0, Math.Max(0, height - boxHeight));

        return new DrawingRectF(x, y, boxWidth, boxHeight);
    }

    private static void FillRounded(DrawingGraphics graphics, DrawingBrush brush, DrawingRectF rect, float radius)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));

        if (radius <= 0)
        {
            graphics.FillRectangle(brush, rect);
            return;
        }

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        float diameter = radius * 2;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        graphics.FillPath(brush, path);
    }
}
