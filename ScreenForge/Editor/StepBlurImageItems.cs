using SkiaSharp;
using ScreenForge.Settings;

namespace ScreenForge.Editor;

// ===================== Numaralı adım işareti =====================
public sealed class StepItem : SceneItem
{
    public int Number { get; set; } = 1;
    public StepShape Shape { get; set; } = StepShape.Circle;
    public SKColor BadgeColor { get; set; } = new(0xFF, 0xE5, 0x48, 0x4D);
    public SKColor NumberColor { get; set; } = SKColors.White;
    public float Diameter { get; set; } = 32f;

    public SKPoint Position { get; set; } // merkez

    private static SKTypeface? _cachedTypeface;
    private static SKTypeface StepTypeface =>
        _cachedTypeface ??= SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    public void SyncBounds()
    {
        float r = Diameter / 2f;
        Bounds = new SKRect(Position.X - r, Position.Y - r, Position.X + r, Position.Y + r);
    }

    public override void Move(float dx, float dy)
    {
        Position = new SKPoint(Position.X + dx, Position.Y + dy);
        SyncBounds();
    }

    public override void Render(SKCanvas canvas)
    {
        SyncBounds();
        canvas.Save();
        ApplyRotation(canvas);

        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = BadgeColor.WithAlpha(AlphaByte), IsAntialias = true };
        using var ring = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.White.WithAlpha((byte)(220 * Opacity)), StrokeWidth = Math.Max(1.5f, Diameter / 16f), IsAntialias = true };

        float r = Diameter / 2f;
        switch (Shape)
        {
            case StepShape.Square:
                var rr = new SKRect(Position.X - r, Position.Y - r, Position.X + r, Position.Y + r);
                canvas.DrawRoundRect(rr, 6, 6, fill);
                canvas.DrawRoundRect(rr, 6, 6, ring);
                break;
            case StepShape.Bubble:
                var bub = new SKRect(Position.X - r, Position.Y - r, Position.X + r, Position.Y + r);
                canvas.DrawRoundRect(bub, r * 0.9f, r * 0.9f, fill);
                // kuyruk
                using (var tail = new SKPath())
                {
                    tail.MoveTo(Position.X - r * 0.3f, Position.Y + r * 0.7f);
                    tail.LineTo(Position.X - r * 0.1f, Position.Y + r * 1.3f);
                    tail.LineTo(Position.X + r * 0.3f, Position.Y + r * 0.7f);
                    tail.Close();
                    canvas.DrawPath(tail, fill);
                }
                break;
            default: // Circle
                canvas.DrawCircle(Position.X, Position.Y, r, fill);
                canvas.DrawCircle(Position.X, Position.Y, r, ring);
                break;
        }

        // Numara
        using var font = new SKFont(StepTypeface, Diameter * 0.55f);
        using var textPaint = new SKPaint { Color = NumberColor.WithAlpha(AlphaByte), IsAntialias = true };
        string s = Number.ToString();
        float tw = font.MeasureText(s);
        float ty = Position.Y - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
        canvas.DrawText(s, Position.X - tw / 2f, ty, SKTextAlign.Left, font, textPaint);

        canvas.Restore();
    }

    public override bool HitTest(SKPoint p)
    {
        float r = Diameter / 2f + 4f;
        return SKPoint.Distance(p, Position) <= r;
    }

    public override SceneItem Clone()
    {
        var c = new StepItem { Number = Number, Shape = Shape, BadgeColor = BadgeColor, NumberColor = NumberColor, Diameter = Diameter, Position = Position };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is StepItem s) { Number = s.Number; Shape = s.Shape; BadgeColor = s.BadgeColor; NumberColor = s.NumberColor; Diameter = s.Diameter; Position = s.Position; }
    }
}

// ===================== Bulanıklaştırma / Pikselleştirme =====================
public sealed class BlurItem : SceneItem
{
    public float Strength { get; set; } = 8f;
    public bool Pixelate { get; set; } = false;

    /// <summary>Altındaki sahneyi içeren anlık görüntü (kompozisyon sırasında atanır).</summary>
    public SKBitmap? SourceSnapshot { get; set; }

    public override void Render(SKCanvas canvas)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        canvas.Save();
        ApplyRotation(canvas);
        canvas.ClipRoundRect(new SKRoundRect(Bounds, 6, 6), antialias: true);

        if (SourceSnapshot != null)
        {
            int bw = SourceSnapshot.Width, bh = SourceSnapshot.Height;
            var src = new SKRectI(
                Math.Clamp((int)Bounds.Left, 0, bw),
                Math.Clamp((int)Bounds.Top, 0, bh),
                Math.Clamp((int)Bounds.Right, 0, bw),
                Math.Clamp((int)Bounds.Bottom, 0, bh));

            if (src.Width < 1 || src.Height < 1) { canvas.Restore(); return; }

            using var cropped = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            SourceSnapshot.ExtractSubset(cropped, src);

            if (Pixelate)
            {
                int sw = Math.Max(1, (int)(src.Width / Strength));
                int sh = Math.Max(1, (int)(src.Height / Strength));
                using var small = cropped.Resize(new SKImageInfo(sw, sh), SKSamplingOptions.Default);
                if (small != null)
                {
                    using var img = SKImage.FromBitmap(small);
                    canvas.DrawImage(img, Bounds, new SKSamplingOptions(SKFilterMode.Nearest));
                }
            }
            else
            {
                using var img = SKImage.FromBitmap(cropped);
                using var filter = SKImageFilter.CreateBlur(Strength, Strength);
                using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
                canvas.DrawImage(img, Bounds, SKSamplingOptions.Default, paint);
            }
        }
        else
        {
            using var ph = new SKPaint { Color = new SKColor(40, 40, 50, 180), IsAntialias = true };
            canvas.DrawRect(Bounds, ph);
        }
        canvas.Restore();
    }

    public override SceneItem Clone()
    {
        var c = new BlurItem { Strength = Strength, Pixelate = Pixelate, SourceSnapshot = SourceSnapshot };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is BlurItem b) { Strength = b.Strength; Pixelate = b.Pixelate; }
    }
}

// ===================== Resim (kolaj öğesi) =====================
public sealed class ImageItem : SceneItem
{
    private SKBitmap _bitmap = null!;
    private SKImage? _cachedImage;

    public SKBitmap Bitmap
    {
        get => _bitmap;
        set { _bitmap = value; _cachedImage?.Dispose(); _cachedImage = null; }
    }

    public ImageItem()
    {
        // Resimlerde varsayılan çerçeve yok (kolaj için temiz görünüm).
        StrokeWidth = 0;
        StrokeColor = SKColors.Transparent;
    }

    /// <summary>Kırpma dikdörtgeni (Bitmap piksel uzayında). Null = tüm resim.</summary>
    public SKRect? CropRect { get; set; }

    public override void Render(SKCanvas canvas)
    {
        if (_bitmap == null) return;
        canvas.Save();
        ApplyRotation(canvas);

        _cachedImage ??= SKImage.FromBitmap(_bitmap);
        var img = _cachedImage;
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(AlphaByte) };

        var hq = new SKSamplingOptions(SKCubicResampler.Mitchell);
        if (CropRect is { } crop)
            canvas.DrawImage(img, crop, Bounds, hq, paint);
        else
            canvas.DrawImage(img, Bounds, hq, paint);

        // ince çerçeve
        if (StrokeWidth > 0 && StrokeColor.Alpha > 0)
        {
            using var border = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true };
            canvas.DrawRect(Bounds, border);
        }
        canvas.Restore();
    }

    public override SceneItem Clone()
    {
        var c = new ImageItem { Bitmap = Bitmap, CropRect = CropRect };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is ImageItem im) { Bitmap = im.Bitmap; CropRect = im.CropRect; }
    }
}
