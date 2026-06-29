using SkiaSharp;

namespace ScreenForge.Editor;

// ===================== Dikdörtgen =====================
public sealed class RectItem : SceneItem
{
    public float CornerRadius { get; set; } = 0f;

    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        if (FillColor.Alpha > 0)
        {
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity)), IsAntialias = true };
            canvas.DrawRoundRect(Bounds, CornerRadius, CornerRadius, fill);
        }
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawRoundRect(Bounds, CornerRadius, CornerRadius, stroke);
        canvas.Restore();
    }

    protected override bool HitTestLocal(SKPoint p)
    {
        if (FillColor.Alpha > 0) return base.HitTestLocal(p);
        // Sadece çerçeve: kenara yakınlık
        float tol = Math.Max(StrokeWidth, 6f);
        var outer = new SKRect(Bounds.Left - tol, Bounds.Top - tol, Bounds.Right + tol, Bounds.Bottom + tol);
        var inner = new SKRect(Bounds.Left + tol, Bounds.Top + tol, Bounds.Right - tol, Bounds.Bottom - tol);
        return outer.Contains(p.X, p.Y) && !inner.Contains(p.X, p.Y);
    }

    public override SceneItem Clone()
    {
        var c = new RectItem { CornerRadius = CornerRadius };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is RectItem r) CornerRadius = r.CornerRadius;
    }
}

// ===================== Elips =====================
public sealed class EllipseItem : SceneItem
{
    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        if (FillColor.Alpha > 0)
        {
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity)), IsAntialias = true };
            canvas.DrawOval(Bounds, fill);
        }
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true };
        canvas.DrawOval(Bounds, stroke);
        canvas.Restore();
    }

    protected override bool HitTestLocal(SKPoint p)
    {
        float rx = Bounds.Width / 2, ry = Bounds.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        float nx = (p.X - Bounds.MidX) / rx, ny = (p.Y - Bounds.MidY) / ry;
        float d = nx * nx + ny * ny;
        if (FillColor.Alpha > 0) return d <= 1.15f;
        return d is >= 0.7f and <= 1.3f; // çerçeveye yakın
    }

    public override SceneItem Clone() { var c = new EllipseItem(); CopyBaseTo(c); return c; }
}

// ===================== Çizgi =====================
public class LineItem : SceneItem
{
    // Çizgi uçları Bounds köşeleriyle ifade edilir: Start=(Left,Top), End=(Right,Bottom)
    // (yön bilgisini korumak için ham noktalar da tutulur)
    public SKPoint Start { get; set; }
    public SKPoint End { get; set; }

    public virtual void SyncBounds()
    {
        Bounds = new SKRect(
            Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y),
            Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
    }

    public override void Move(float dx, float dy)
    {
        Start = new SKPoint(Start.X + dx, Start.Y + dy);
        End = new SKPoint(End.X + dx, End.Y + dy);
        SyncBounds();
    }

    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(Start, End, paint);
        canvas.Restore();
    }

    public override bool HitTest(SKPoint p)
    {
        return DistanceToSegment(ToLocal(p), Start, End) <= Math.Max(StrokeWidth, 6f);
    }

    /// <summary>Noktanın [a,b] doğru parçasına uzaklığı (paylaşılan yardımcı).</summary>
    public static float Distance2(SKPoint p, SKPoint a, SKPoint b) => DistanceToSegment(p, a, b);

    protected static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 == 0) return SKPoint.Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        var proj = new SKPoint(a.X + t * dx, a.Y + t * dy);
        return SKPoint.Distance(p, proj);
    }

    public override SceneItem Clone()
    {
        var c = new LineItem { Start = Start, End = End };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is LineItem l) { Start = l.Start; End = l.End; }
    }
}

// ===================== Ok =====================
public sealed class ArrowItem : LineItem
{
    /// <summary>Ok başı boyut çarpanı.</summary>
    public float HeadScale { get; set; } = 1f;

    /// <summary>Eğme kontrol noktası. null = düz çizgi.</summary>
    public SKPoint? BendPoint { get; set; }

    /// <summary>Eğme tutamacının ekran konumu (çizimde güncellenir, InteractiveCanvas kullanır).</summary>
    public SKPoint BendHandlePos { get; set; }

    /// <summary>
    /// BendPoint varsa quadratic bezier'in gerçek bounding box'ını hesaplar;
    /// yoksa LineItem.SyncBounds() kullanır.
    /// </summary>
    public override void SyncBounds()
    {
        if (BendPoint is not { } cp)
        {
            base.SyncBounds();
            return;
        }

        // Quadratic bezier extrema: t = (P0 - P1) / (P0 - 2*P1 + P2)
        // Her eksen için: min/max(P0, P2, bezier(t)) — t ∈ (0,1) ise geçerli.
        float minX = Math.Min(Start.X, End.X);
        float maxX = Math.Max(Start.X, End.X);
        float minY = Math.Min(Start.Y, End.Y);
        float maxY = Math.Max(Start.Y, End.Y);

        float denomX = Start.X - 2f * cp.X + End.X;
        if (Math.Abs(denomX) > 1e-5f)
        {
            float tx = (Start.X - cp.X) / denomX;
            if (tx > 0f && tx < 1f)
            {
                float bx = BezierAt(Start.X, cp.X, End.X, tx);
                minX = Math.Min(minX, bx);
                maxX = Math.Max(maxX, bx);
            }
        }

        float denomY = Start.Y - 2f * cp.Y + End.Y;
        if (Math.Abs(denomY) > 1e-5f)
        {
            float ty = (Start.Y - cp.Y) / denomY;
            if (ty > 0f && ty < 1f)
            {
                float by = BezierAt(Start.Y, cp.Y, End.Y, ty);
                minY = Math.Min(minY, by);
                maxY = Math.Max(maxY, by);
            }
        }

        Bounds = new SKRect(minX, minY, maxX, maxY);
    }

    private static float BezierAt(float p0, float p1, float p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

        if (BendPoint is { } cp)
        {
            var stopPoint = DrawArrowHead(canvas, cp, End);
            using var path = new SKPath();
            path.MoveTo(Start);
            path.QuadTo(cp, stopPoint);
            canvas.DrawPath(path, paint);
        }
        else
        {
            var stopPoint = DrawArrowHead(canvas, Start, End);
            canvas.DrawLine(Start, stopPoint, paint);
        }

        BendHandlePos = BendPoint ?? new SKPoint((Start.X + End.X) / 2f, (Start.Y + End.Y) / 2f);
        SyncBounds();
        canvas.Restore();
    }

    /// <summary>Ok başını çizer ve gövdenin durması gereken noktayı (ok tabanı merkezi) döndürür.</summary>
    private SKPoint DrawArrowHead(SKCanvas canvas, SKPoint from, SKPoint to)
    {
        float headLen = Math.Max(14f, StrokeWidth * 4f) * HeadScale;
        double ang = Math.Atan2(to.Y - from.Y, to.X - from.X);
        double spread = Math.PI / 7;
        var p1 = new SKPoint(to.X - headLen * (float)Math.Cos(ang - spread), to.Y - headLen * (float)Math.Sin(ang - spread));
        var p2 = new SKPoint(to.X - headLen * (float)Math.Cos(ang + spread), to.Y - headLen * (float)Math.Sin(ang + spread));
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = StrokeColor.WithAlpha(AlphaByte), IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(to); path.LineTo(p1); path.LineTo(p2); path.Close();
        canvas.DrawPath(path, fill);
        // Gövdenin durması gereken nokta: ok tabanı merkezi (headLen * cos(spread) ≈ 0.85)
        return new SKPoint(
            to.X - headLen * 0.85f * (float)Math.Cos(ang),
            to.Y - headLen * 0.85f * (float)Math.Sin(ang));
    }

    public override void Move(float dx, float dy)
    {
        base.Move(dx, dy); // Start, End, Bounds güncellenir
        if (BendPoint is { } cp)
            BendPoint = new SKPoint(cp.X + dx, cp.Y + dy);
        // BendPoint dahil Bounds yeniden hesapla
        SyncBounds();
    }

    public override SceneItem Clone()
    {
        var c = new ArrowItem { Start = Start, End = End, HeadScale = HeadScale, BendPoint = BendPoint };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is ArrowItem a) { HeadScale = a.HeadScale; BendPoint = a.BendPoint; }
    }
}

// ===================== Serbest kalem =====================
public class FreehandItem : SceneItem
{
    public List<SKPoint> Points { get; set; } = new();

    public void AddPoint(SKPoint p)
    {
        if (Points.Count > 0 && SKPoint.Distance(Points[^1], p) < 3f) return;
        Points.Add(p);
        RecalcBounds();
    }

    protected virtual void RecalcBounds()
    {
        if (Points.Count == 0) { Bounds = SKRect.Empty; return; }
        float minX = Points[0].X, minY = Points[0].Y, maxX = minX, maxY = minY;
        foreach (var pt in Points)
        {
            minX = Math.Min(minX, pt.X); minY = Math.Min(minY, pt.Y);
            maxX = Math.Max(maxX, pt.X); maxY = Math.Max(maxY, pt.Y);
        }
        Bounds = new SKRect(minX, minY, maxX, maxY);
    }

    public override void Move(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new SKPoint(Points[i].X + dx, Points[i].Y + dy);
        RecalcBounds();
    }

    /// <summary>Chaikin yumuşatma ile yol üretir (2 pass).</summary>
    protected SKPath BuildPath()
    {
        var path = new SKPath();
        if (Points.Count == 0) return path;
        if (Points.Count == 1) { path.AddCircle(Points[0].X, Points[0].Y, StrokeWidth / 2f); return path; }
        if (Points.Count == 2) { path.MoveTo(Points[0]); path.LineTo(Points[1]); return path; }

        // Chaikin smoothing — 2 pass
        var pts = Points.ToList();
        for (int pass = 0; pass < 2; pass++)
        {
            var smooth = new List<SKPoint> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                smooth.Add(new SKPoint(a.X * 0.75f + b.X * 0.25f, a.Y * 0.75f + b.Y * 0.25f));
                smooth.Add(new SKPoint(a.X * 0.25f + b.X * 0.75f, a.Y * 0.25f + b.Y * 0.75f));
            }
            smooth.Add(pts[^1]);
            pts = smooth;
        }

        path.MoveTo(pts[0]);
        for (int i = 1; i < pts.Count - 2; i++)
        {
            var mid = new SKPoint((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
            path.QuadTo(pts[i].X, pts[i].Y, mid.X, mid.Y);
        }
        path.LineTo(pts[^1]);
        return path;
    }

    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        using var path = BuildPath();
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, Color = StrokeColor.WithAlpha(AlphaByte), StrokeWidth = StrokeWidth, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    public override bool HitTest(SKPoint p)
    {
        var local = ToLocal(p);
        float tol = Math.Max(StrokeWidth, 8f);
        for (int i = 0; i < Points.Count - 1; i++)
            if (LineItem.Distance2(local, Points[i], Points[i + 1]) <= tol) return true;
        return Points.Count == 1 && SKPoint.Distance(local, Points[0]) <= tol;
    }

    public override SceneItem Clone()
    {
        var c = new FreehandItem { Points = new List<SKPoint>(Points) };
        CopyBaseTo(c);
        return c;
    }

    public override void RestoreFrom(SceneItem other)
    {
        base.RestoreFrom(other);
        if (other is FreehandItem f) Points = new List<SKPoint>(f.Points);
    }
}

// ===================== Fosforlu kalem =====================
public sealed class HighlightItem : FreehandItem
{
    protected override void RecalcBounds()
    {
        base.RecalcBounds();
        if (Bounds.IsEmpty) return;
        float pad = StrokeWidth * 2f;
        Bounds = new SKRect(Bounds.Left - pad, Bounds.Top - pad, Bounds.Right + pad, Bounds.Bottom + pad);
    }

    public override void Render(SKCanvas canvas)
    {
        canvas.Save();
        ApplyRotation(canvas);
        using var path = BuildPath();
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = StrokeColor.WithAlpha((byte)(120 * Opacity)),
            StrokeWidth = StrokeWidth * 4f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    public override SceneItem Clone()
    {
        var c = new HighlightItem { Points = new List<SKPoint>(Points) };
        CopyBaseTo(c);
        return c;
    }
}
