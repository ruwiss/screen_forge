using SkiaSharp;
using ScreenForge.Editor;

namespace ScreenForge.Presenter;

public sealed class StrokeArrowItem : SceneItem
{
    public List<SKPoint> Points { get; set; } = [];
    public float HeadScale { get; set; } = 1f;

    public void SyncBounds()
    {
        if (Points.Count == 0) { Bounds = SKRect.Empty; return; }
        float minX = Points[0].X, minY = Points[0].Y, maxX = minX, maxY = minY;
        foreach (var p in Points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        Bounds = new SKRect(minX, minY, maxX, maxY);
    }

    public override void Move(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new SKPoint(Points[i].X + dx, Points[i].Y + dy);
        SyncBounds();
    }

    public override void Render(SKCanvas canvas)
    {
        if (Points.Count < 2) return;
        canvas.Save();
        ApplyRotation(canvas);
        var tip = Points[^1];
        var from = PointBack(Points, Math.Max(18f, StrokeWidth * 5f));
        var stop = DrawHead(canvas, from, tip);
        using var path = new SKPath();
        path.MoveTo(Points[0]);
        for (int i = 1; i < Points.Count - 1; i++)
            path.LineTo(Points[i]);
        path.LineTo(stop);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = StrokeColor.WithAlpha(AlphaByte),
            StrokeWidth = StrokeWidth,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    private static SKPoint PointBack(List<SKPoint> pts, float dist)
    {
        float acc = 0;
        for (int i = pts.Count - 1; i > 0; i--)
        {
            acc += SKPoint.Distance(pts[i], pts[i - 1]);
            if (acc >= dist) return pts[i - 1];
        }
        return pts[0];
    }

    private SKPoint DrawHead(SKCanvas canvas, SKPoint from, SKPoint to)
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
        return new SKPoint(
            to.X - headLen * 0.85f * (float)Math.Cos(ang),
            to.Y - headLen * 0.85f * (float)Math.Sin(ang));
    }

    public override SceneItem Clone()
    {
        var c = new StrokeArrowItem { Points = [.. Points], HeadScale = HeadScale };
        CopyBaseTo(c);
        return c;
    }
}
