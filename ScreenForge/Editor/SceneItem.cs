using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Tüm tuval öğelerinin tabanı. Eksen-hizalı kutu (Bounds) + isteğe bağlı döndürme.
/// Immediate-mode: her öğe kendini bir SKCanvas üzerine çizer.
/// </summary>
public abstract class SceneItem
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Eksen-hizalı sınırlar (tuval/içerik piksel uzayında).</summary>
    public SKRect Bounds { get; set; }

    /// <summary>Derece cinsinden döndürme (merkez etrafında).</summary>
    public float Rotation { get; set; }

    // Ortak stil
    public SKColor StrokeColor { get; set; } = new(0xFF, 0x2F, 0x6F, 0xED);
    public SKColor FillColor { get; set; } = SKColors.Transparent;
    public float StrokeWidth { get; set; } = 3f;
    public float Opacity { get; set; } = 1f;

    /// <summary>Döndürme dahil gerçek köşe noktaları (seçim/hit-test için).</summary>
    public SKPoint Center => new(Bounds.MidX, Bounds.MidY);

    /// <summary>Bu öğe metin düzenleme destekliyor mu (çift tık).</summary>
    public virtual bool IsTextEditable => false;

    /// <summary>En küçük boyut (resize sırasında çökmemesi için).</summary>
    public virtual float MinSize => 4f;

    public abstract void Render(SKCanvas canvas);

    /// <summary>Nokta bu öğenin üstünde mi? (döndürme dikkate alınır)</summary>
    public virtual bool HitTest(SKPoint p)
    {
        var local = ToLocal(p);
        return HitTestLocal(local);
    }

    /// <summary>Döndürmeyi geri alıp yerel (eksen-hizalı) uzaya çevirir.</summary>
    protected SKPoint ToLocal(SKPoint p)
    {
        if (Rotation == 0) return p;
        var c = Center;
        float rad = -Rotation * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        float dx = p.X - c.X, dy = p.Y - c.Y;
        return new SKPoint(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
    }

    /// <summary>Yerel uzayda hit-test (varsayılan: dolgulu kutu + çizgi toleransı).</summary>
    protected virtual bool HitTestLocal(SKPoint local)
    {
        var r = Bounds;
        float tol = Math.Max(StrokeWidth, 6f);
        var inflated = new SKRect(r.Left - tol, r.Top - tol, r.Right + tol, r.Bottom + tol);
        return inflated.Contains(local.X, local.Y);
    }

    public virtual void Move(float dx, float dy)
    {
        Bounds = new SKRect(Bounds.Left + dx, Bounds.Top + dy, Bounds.Right + dx, Bounds.Bottom + dy);
    }

    /// <summary>Döndürme uygulanmış canvas bağlamı kurar (Render içinde kullanılır).</summary>
    protected void ApplyRotation(SKCanvas canvas)
    {
        if (Rotation != 0)
            canvas.RotateDegrees(Rotation, Center.X, Center.Y);
    }

    /// <summary>Undo için derin kopya.</summary>
    public abstract SceneItem Clone();

    protected void CopyBaseTo(SceneItem t)
    {
        t.Bounds = Bounds;
        t.Rotation = Rotation;
        t.StrokeColor = StrokeColor;
        t.FillColor = FillColor;
        t.StrokeWidth = StrokeWidth;
        t.Opacity = Opacity;
    }

    /// <summary>Bu öğenin durumunu başka örnekten geri yükler (undo/redo).</summary>
    public virtual void RestoreFrom(SceneItem other)
    {
        Bounds = other.Bounds;
        Rotation = other.Rotation;
        StrokeColor = other.StrokeColor;
        FillColor = other.FillColor;
        StrokeWidth = other.StrokeWidth;
        Opacity = other.Opacity;
    }

    protected byte AlphaByte => (byte)Math.Clamp(Opacity * 255f, 0, 255);
}
