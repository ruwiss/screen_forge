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

    public SKPoint Position { get; set; }

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
                using (var tail = new SKPath())
                {
                    tail.MoveTo(Position.X - r * 0.3f, Position.Y + r * 0.7f);
                    tail.LineTo(Position.X - r * 0.1f, Position.Y + r * 1.3f);
                    tail.LineTo(Position.X + r * 0.3f, Position.Y + r * 0.7f);
                    tail.Close();
                    canvas.DrawPath(tail, fill);
                }
                break;
            default:
                canvas.DrawCircle(Position.X, Position.Y, r, fill);
                canvas.DrawCircle(Position.X, Position.Y, r, ring);
                break;
        }

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
/// <summary>
/// Blur, ImageItem gibi çalışır: içerik bir kez "pişirilir" (Baked),
/// taşıma/resize yalnızca Bounds + DrawImage. Paint yolunda asla
/// snapshot capture / Resize / CreateBlur yok → sürüklerken crash olmaz.
/// </summary>
public sealed class BlurItem : SceneItem
{
    private float _strength = 8f;
    private bool _pixelate;
    private SKBitmap? _baked;
    private SKImage? _drawImage;

    public float Strength
    {
        get => _strength;
        set
        {
            float v = Math.Clamp(value, 1f, 64f);
            if (Math.Abs(_strength - v) < 0.01f) return;
            _strength = v;
            NeedsBake = true;
        }
    }

    public bool Pixelate
    {
        get => _pixelate;
        set
        {
            if (_pixelate == value) return;
            _pixelate = value;
            NeedsBake = true;
        }
    }

    /// <summary>true ise bir sonraki BakeDirtyBlurs içeriği yeniden üretir.</summary>
    public bool NeedsBake { get; set; } = true;

    /// <summary>
    /// Taşıma/resize sürüklerken true: eski baked yerine yarı saydam cam önizleme
    /// (alt içerik hafif görünür). Bırakınca false + bake.
    /// </summary>
    public bool DragPreview { get; set; }

    /// <summary>Test / dış okuma: pişmiş bitmap (yoksa null).</summary>
    public SKBitmap? BakedBitmap => _baked;

    /// <summary>Eski API uyumu — bazı testler SourceSnapshot arıyordu.</summary>
    public SKBitmap? SourceSnapshot => _baked;

    public void ClearBaked()
    {
        _drawImage?.Dispose();
        _drawImage = null;
        _baked?.Dispose();
        _baked = null;
        NeedsBake = true;
    }

    /// <summary>Bake sonucu — ownership BlurItem'a geçer.</summary>
    public void SetBaked(SKBitmap? bmp)
    {
        _drawImage?.Dispose();
        _drawImage = null;
        if (_baked != null && !ReferenceEquals(_baked, bmp))
        {
            try { _baked.Dispose(); } catch { /* ignore */ }
        }
        _baked = bmp;
        NeedsBake = bmp == null;
    }

    public override void Render(SKCanvas canvas)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;

        canvas.Save();
        try
        {
            ApplyRotation(canvas);
            using var rr = new SKRoundRect(Bounds, 6, 6);
            canvas.ClipRoundRect(rr, antialias: true);

            // Sürükleme: yarı saydam cam — arkadaki sahne görünsün; baked çizme.
            if (DragPreview || _baked == null)
            {
                DrawGhostPreview(canvas);
                return;
            }

            _drawImage ??= SKImage.FromBitmap(_baked);
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(AlphaByte),
                IsAntialias = true,
            };
            canvas.DrawImage(_drawImage, Bounds, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        catch
        {
            try { DrawGhostPreview(canvas); } catch { /* ignore */ }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Yarı saydam buzlu cam + ince kenar — alt içerik okunabilir kalsın.</summary>
    private void DrawGhostPreview(SKCanvas canvas)
    {
        byte fillA = (byte)Math.Clamp(95 * Opacity, 20, 160);
        byte strokeA = (byte)Math.Clamp(140 * Opacity, 40, 200);

        using var fill = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(28, 32, 42, fillA),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(Bounds, 6, 6, fill);

        // Hafif “blur maskesi” hissi için ikinci yarı saydam katman
        using var tint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(180, 190, 210, (byte)Math.Clamp(35 * Opacity, 10, 80)),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(Bounds, 6, 6, tint);

        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, strokeA),
            StrokeWidth = 1.25f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0),
        };
        canvas.DrawRoundRect(Bounds, 6, 6, stroke);
    }

    public override SceneItem Clone()
    {
        // Baked kopyalanmaz — ağır ve dispose riski. Clone sadece stil/bounds.
        var c = new BlurItem { Strength = Strength, Pixelate = Pixelate, NeedsBake = true };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is BlurItem b)
        {
            _strength = b._strength;
            _pixelate = b._pixelate;
        }
        // Bounds / stil değişti → yeniden pişir (taşı/resize commit, undo).
        NeedsBake = true;
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
        StrokeWidth = 0;
        StrokeColor = SKColors.Transparent;
    }

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

        if (StrokeWidth > 0 && StrokeColor.Alpha > 0)
        {
            using var border = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true };
            canvas.DrawRect(Bounds, border);
        }
        canvas.Restore();
    }

    public override SceneItem Clone()
    {
        var c = new ImageItem { CropRect = CropRect };
        if (_bitmap != null)
            c.Bitmap = _bitmap.Copy() ?? _bitmap;
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is ImageItem im)
        {
            Bitmap = im.Bitmap;
            CropRect = im.CropRect;
        }
    }
}
