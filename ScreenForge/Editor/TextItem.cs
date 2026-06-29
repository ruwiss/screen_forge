using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Modern, düzenlenebilir metin öğesi. Lightshot'un düz metninden farklı olarak:
/// gölge, kontur (stroke) ve paddingli yuvarlatılmış şerit arka planı destekler.
/// Yazıldıktan sonra tekrar seçilip taşınabilir/düzenlenebilir.
/// </summary>
public sealed class TextItem : SceneItem
{
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 28f;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }

    public bool Shadow { get; set; } = true;
    public int ShadowLevel { get; set; } = 1;   // 0=Hafif, 1=Normal, 2=Güçlü
    public bool StrokeText { get; set; }
    public SKColor StrokeTextColor { get; set; } = SKColors.Black;

    public bool Ribbon { get; set; } = true;
    public SKColor RibbonColor { get; set; } = new(0x1F, 0x24, 0x30, 0xCC);
    public float RibbonPadding { get; set; } = 8f;
    public float RibbonPaddingV { get; set; } = 4f;
    public float RibbonRadius { get; set; } = 8f;

    public override bool IsTextEditable => true;

    /// <summary>Sol-üst köşe (metin bu noktadan başlar). Bounds buradan hesaplanır.</summary>
    public SKPoint Position { get; set; }

    public override void Move(float dx, float dy)
    {
        Position = new SKPoint(Position.X + dx, Position.Y + dy);
        // Bounds bir sonraki Measure/Render'da güncellenir.
        base.Move(dx, dy);
    }

    private SKFont BuildFont()
    {
        var style = new SKFontStyle(
            Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = SKTypeface.FromFamilyName(FontFamily, style) ?? SKTypeface.Default;
        return new SKFont(typeface, FontSize);
    }

    /// <summary>Metni ölçer ve Bounds'u günceller (Render öncesi de çağrılır).</summary>
    public SKSize Measure()
    {
        using var font = BuildFont();
        var lines = (Text.Length == 0 ? " " : Text).Split('\n');
        float lineHeight = font.Spacing;
        float maxW = 0;
        foreach (var line in lines)
        {
            float w = font.MeasureText(line.Length == 0 ? " " : line);
            maxW = Math.Max(maxW, w);
        }
        float totalH = lineHeight * lines.Length;
        var size = new SKSize(maxW, totalH);

        float padH = Ribbon ? RibbonPadding : 0;
        float padV = Ribbon ? RibbonPaddingV : 0;
        Bounds = new SKRect(
            Position.X - padH, Position.Y - padV,
            Position.X + size.Width + padH, Position.Y + size.Height + padV);
        return size;
    }

    public override void Render(SKCanvas canvas)
    {
        Measure();
        canvas.Save();
        ApplyRotation(canvas);

        using var font = BuildFont();
        var lines = Text.Split('\n');
        float lineHeight = font.Spacing;

        // Şerit arka plan
        if (Ribbon && Text.Length > 0)
        {
            using var ribbonPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = RibbonColor.WithAlpha((byte)(RibbonColor.Alpha * Opacity)), IsAntialias = true };
            canvas.DrawRoundRect(Bounds, RibbonRadius, RibbonRadius, ribbonPaint);
        }

        // Metin satırları
        float baselineY = Position.Y - font.Metrics.Ascent;
        for (int i = 0; i < lines.Length; i++)
        {
            float y = baselineY + i * lineHeight;
            DrawLine(canvas, lines[i], Position.X, y, font);
        }

        canvas.Restore();
    }

    private void DrawLine(SKCanvas canvas, string text, float x, float y, SKFont font)
    {
        if (text.Length == 0) return;

        if (Shadow)
        {
            float dy, blur; byte alpha;
            switch (ShadowLevel)
            {
                case 0: dy = 1; blur = 2; alpha = (byte)(100 * Opacity); break;
                case 2: dy = 3; blur = 6; alpha = (byte)(220 * Opacity); break;
                default: dy = 2; blur = 3; alpha = (byte)(160 * Opacity); break;
            }
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, alpha), IsAntialias = true };
            shadow.ImageFilter = SKImageFilter.CreateDropShadow(0, dy, blur, blur, new SKColor(0, 0, 0, alpha));
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, shadow);
        }

        // Kontur
        if (StrokeText)
        {
            using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeTextColor.WithAlpha(AlphaByte), StrokeWidth = Math.Max(2f, FontSize / 12f), IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, strokePaint);
        }

        // Dolgu (metin rengi = StrokeColor alanını metin rengi olarak kullanır)
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = StrokeColor.WithAlpha(AlphaByte), IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, fillPaint);
    }

    public override bool HitTest(SKPoint p)
    {
        Measure();
        return base.HitTest(p);
    }

    public override SceneItem Clone()
    {
        var c = new TextItem
        {
            Text = Text, FontFamily = FontFamily, FontSize = FontSize, Bold = Bold, Italic = Italic,
            Shadow = Shadow, ShadowLevel = ShadowLevel, StrokeText = StrokeText, StrokeTextColor = StrokeTextColor,
            Ribbon = Ribbon, RibbonColor = RibbonColor, RibbonPadding = RibbonPadding, RibbonPaddingV = RibbonPaddingV, RibbonRadius = RibbonRadius,
            Position = Position,
        };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is TextItem t)
        {
            Text = t.Text; FontFamily = t.FontFamily; FontSize = t.FontSize; Bold = t.Bold; Italic = t.Italic;
            Shadow = t.Shadow; ShadowLevel = t.ShadowLevel; StrokeText = t.StrokeText; StrokeTextColor = t.StrokeTextColor;
            Ribbon = t.Ribbon; RibbonColor = t.RibbonColor; RibbonPadding = t.RibbonPadding; RibbonPaddingV = t.RibbonPaddingV; RibbonRadius = t.RibbonRadius;
            Position = t.Position;
        }
    }
}
