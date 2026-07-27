using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.Brush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingPoint = System.Drawing.PointF;
using DrawingRect = System.Drawing.Rectangle;
using DrawingRectF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.SizeF;

namespace ScreenForge.Gif.Input;

/// <summary>
/// Girdi kaplamasının görünüm ayarları.
/// Varsayılan renkler ve boyutlar ScreenToGif ile aynıdır.
/// </summary>
public sealed class InputOverlayOptions
{
    /// <summary>Fare tıklamalarını renkli daire ile vurgula.</summary>
    public bool HighlightClicks { get; init; } = true;

    /// <summary>
    /// Tıklama olmasa da imlecin etrafında sürekli bir vurgu göster.
    /// ScreenToGif'te varsayılan kapalıdır.
    /// </summary>
    public bool HighlightCursor { get; init; }

    /// <summary>Basılan tuşları köşede rozet olarak göster.</summary>
    public bool ShowKeys { get; init; } = true;

    /// <summary>Vurgu dairesinin yarıçapı (kaynak piksel).</summary>
    public double Radius { get; init; } = 12;

    /// <summary>Tıklama yokken imleci saran vurgu rengi.</summary>
    public DrawingColor CursorHighlightColor { get; init; } = DrawingColor.FromArgb(120, 255, 255, 0);

    public DrawingColor LeftClickColor { get; init; } = DrawingColor.FromArgb(120, 255, 255, 0);
    public DrawingColor RightClickColor { get; init; } = DrawingColor.FromArgb(120, 255, 0, 0);
    public DrawingColor MiddleClickColor { get; init; } = DrawingColor.FromArgb(120, 0, 255, 255);
    public DrawingColor FirstExtraClickColor { get; init; } = DrawingColor.FromArgb(120, 255, 0, 128);
    public DrawingColor SecondExtraClickColor { get; init; } = DrawingColor.FromArgb(120, 255, 128, 0);

    /// <summary>Tuş rozeti yazı boyutu (kaynak piksel).</summary>
    public float KeyFontSize { get; init; } = 14f;

    public bool HasWork => HighlightClicks || HighlightCursor || ShowKeys;

    /// <summary>
    /// Basılı düğmeye karşılık gelen vurgu rengi.
    /// Hiçbiri basılı değilse imleç vurgu rengi döner.
    /// </summary>
    public DrawingColor ColorFor(MouseButtons buttons) => buttons switch
    {
        _ when (buttons & MouseButtons.Left) != 0 => LeftClickColor,
        _ when (buttons & MouseButtons.Right) != 0 => RightClickColor,
        _ when (buttons & MouseButtons.Middle) != 0 => MiddleClickColor,
        _ when (buttons & MouseButtons.Extra1) != 0 => FirstExtraClickColor,
        _ when (buttons & MouseButtons.Extra2) != 0 => SecondExtraClickColor,
        _ => CursorHighlightColor,
    };
}

/// <summary>
/// Dışa aktarım sırasında kare piksellerinin üzerine tıklama vurgusu ve tuş
/// rozetleri çizer. Kayıt sırasında değil, kodlamadan hemen önce uygulanır;
/// böylece kullanıcı ayarı değiştirip yeniden dışa aktarabilir.
/// </summary>
internal static class InputOverlayRenderer
{
    /// <summary>
    /// Kareyi kopyalayıp üzerine kaplamayı çizer. Girdi yoksa özgün dizi döner.
    /// </summary>
    /// <param name="bgra">BGRA kare verisi.</param>
    /// <param name="width">Kare genişliği.</param>
    /// <param name="height">Kare yüksekliği.</param>
    /// <param name="input">Bu kareye ait girdi bilgisi.</param>
    /// <param name="options">Görünüm ayarları.</param>
    /// <param name="scale">Kaynak boyuttan çıktı boyutuna ölçek (yeniden boyutlandırma için).</param>
    public static byte[] Apply(byte[] bgra, int width, int height, FrameInput? input,
        InputOverlayOptions options, double scale = 1.0)
    {
        if (input == null || !options.HasWork || width <= 0 || height <= 0)
            return bgra;

        bool clicking = input.Buttons != MouseButtons.None || input.ClickStarted;

        // Tıklama vurgusu yalnızca düğme basılıyken; imleç vurgusu ise imleç
        // göründüğü sürece çizilir.
        bool drawClick = options.HighlightClicks && input.CursorVisible && clicking;
        bool drawCursor = options.HighlightCursor && input.CursorVisible && !clicking;
        bool drawKeys = options.ShowKeys && input.Keys.Count > 0;

        if (!drawClick && !drawCursor && !drawKeys)
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

            if (drawClick || drawCursor)
                DrawHighlight(graphics, input, options, scale);

            if (drawKeys)
                DrawKeyBadge(graphics, input.Keys, width, height, options, scale);
        }
        finally
        {
            handle.Free();
        }

        // GDI+ alfayı bozabilir; GIF yolunda tüm pikseller opak olmalı.
        for (int i = 3; i < output.Length; i += 4) output[i] = 255;
        return output;
    }

    /// <summary>
    /// İmlecin üzerine yarı saydam dolu daire çizer.
    /// Renk basılı düğmeye göre seçilir; düğme yoksa imleç vurgu rengi kullanılır.
    /// </summary>
    private static void DrawHighlight(DrawingGraphics graphics, FrameInput input,
        InputOverlayOptions options, double scale)
    {
        var color = options.ColorFor(input.Buttons);
        if (color.A == 0)
            return;

        float x = (float)(input.CursorX * scale);
        float y = (float)(input.CursorY * scale);
        float radius = (float)Math.Max(2, options.Radius * scale);

        using var brush = new System.Drawing.SolidBrush(color);
        graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
    }

    private static void DrawKeyBadge(DrawingGraphics graphics, List<string> keys,
        int width, int height, InputOverlayOptions options, double scale)
    {
        string text = string.Join(" + ", keys);
        float fontSize = (float)Math.Max(8, options.KeyFontSize * scale);

        using var font = new DrawingFont("Segoe UI", fontSize, System.Drawing.FontStyle.Bold,
            System.Drawing.GraphicsUnit.Pixel);

        var textSize = graphics.MeasureString(text, font);
        float padX = fontSize * 0.7f;
        float padY = fontSize * 0.4f;
        float margin = fontSize * 0.9f;

        float boxWidth = textSize.Width + padX * 2;
        float boxHeight = textSize.Height + padY * 2;
        float boxX = margin;
        float boxY = height - boxHeight - margin;

        if (boxX + boxWidth > width) boxX = Math.Max(0, width - boxWidth);
        if (boxY < 0) boxY = 0;

        var box = new DrawingRectF(boxX, boxY, boxWidth, boxHeight);

        using (var background = new System.Drawing.SolidBrush(DrawingColor.FromArgb(205, 12, 14, 20)))
            FillRounded(graphics, background, box, fontSize * 0.35f);

        using var foreground = new System.Drawing.SolidBrush(DrawingColor.White);
        graphics.DrawString(text, font, foreground, new DrawingPoint(boxX + padX, boxY + padY));
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
