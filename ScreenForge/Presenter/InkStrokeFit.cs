using SkiaSharp;
using ScreenForge.Editor;

namespace ScreenForge.Presenter;

public static class InkStrokeFit
{
    public static SceneItem? TryFit(IReadOnlyList<SKPoint> pts, SceneItem style, IReadOnlyList<SKPoint>? extra = null)
    {
        if (extra is { Count: >= 2 } && pts.Count >= 3
            && PathLength(extra) <= PathLength(pts) * 0.7f
            && HeadNearTip(pts, extra)
            && TryArrow(Merge(pts, extra), style, tipAt: pts.Count - 1) is { } paired)
            return paired;

        if (pts.Count < 4) return null;
        float len = PathLength(pts);
        if (len < 60f) return null;
        if (IsClosed(pts, len)) return null;

        if (TryArrow(pts, style) is { } arrow)
            return arrow;

        float chord = SKPoint.Distance(pts[0], pts[^1]);
        if (chord >= 56f && chord / len >= 0.88f)
        {
            var line = new LineItem { Start = pts[0], End = pts[^1] };
            CopyStyle(line, style);
            line.SyncBounds();
            return line;
        }
        return null;
    }

    private static bool IsClosed(IReadOnlyList<SKPoint> pts, float len) =>
        SKPoint.Distance(pts[0], pts[^1]) <= Math.Max(28f, len * 0.14f);

    private static bool HeadNearTip(IReadOnlyList<SKPoint> shaft, IReadOnlyList<SKPoint> head)
    {
        var tip = shaft[^1];
        float lim = Math.Max(36f, PathLength(shaft) * 0.16f);
        return SKPoint.Distance(tip, head[0]) <= lim || SKPoint.Distance(tip, head[^1]) <= lim;
    }

    private static List<SKPoint> Merge(IReadOnlyList<SKPoint> a, IReadOnlyList<SKPoint> b)
    {
        var n = new List<SKPoint>(a.Count + b.Count);
        n.AddRange(a);
        if (SKPoint.Distance(a[^1], b[0]) <= SKPoint.Distance(a[^1], b[^1]))
            n.AddRange(b);
        else
        {
            for (int i = b.Count - 1; i >= 0; i--)
                n.Add(b[i]);
        }
        return n;
    }

    private static SceneItem? TryArrow(IReadOnlyList<SKPoint> pts, SceneItem style, int? tipAt = null)
    {
        if (pts.Count < 5) return null;
        int tipI = tipAt ?? FindApex(pts);
        if (tipI < 2 || tipI >= pts.Count - 1) return null;
        if (!ShaftDir(pts, tipI, out float sx, out float sy)) return null;
        tipI = TrimBarb(pts, tipI, sx, sy);
        if (tipI < 2 || tipI >= pts.Count - 1) return null;
        if (!ShaftDir(pts, tipI, out sx, out sy)) return null;
        if (!HeadOk(pts, tipI, sx, sy, tipAt.HasValue)) return null;
        return FinishArrow(pts, tipI, style);
    }

    private static bool HeadOk(IReadOnlyList<SKPoint> pts, int tipI, float sx, float sy, bool twoStroke)
    {
        float total = PathLength(pts);
        float shaftLen = PathLength(pts, 0, tipI);
        float headLen = PathLength(pts, tipI, pts.Count - 1);
        if (shaftLen < 48f) return false;
        if (headLen < Math.Max(12f, shaftLen * 0.05f)) return false;
        if (headLen > total * (twoStroke ? 0.55f : 0.50f)) return false;

        var tip = pts[tipI];
        int back = 0;
        float maxPerp = 0, maxBack = 0, maxFromTip = 0;
        for (int i = tipI + 1; i < pts.Count; i++)
        {
            float vx = pts[i].X - tip.X;
            float vy = pts[i].Y - tip.Y;
            float along = vx * sx + vy * sy;
            float perp = MathF.Abs(vx * sy - vy * sx);
            float dist = MathF.Sqrt(vx * vx + vy * vy);
            if (along < 0) { back++; maxBack = Math.Max(maxBack, -along); }
            if (perp > maxPerp) maxPerp = perp;
            if (dist > maxFromTip) maxFromTip = dist;
        }
        if (back < 1) return false;
        if (maxBack < Math.Max(6f, shaftLen * 0.025f)) return false;
        if (maxPerp < Math.Max(6f, shaftLen * 0.025f)) return false;
        if (maxFromTip > shaftLen * 0.42f) return false;
        return true;
    }

    private static SceneItem FinishArrow(IReadOnlyList<SKPoint> pts, int tipI, SceneItem style)
    {
        var start = pts[0];
        var tip = pts[tipI];
        float cx = tip.X - start.X, cy = tip.Y - start.Y;
        float cl = MathF.Sqrt(cx * cx + cy * cy);
        if (cl < 8f)
            return PolyArrow(pts, tipI, style);

        float nx = -cy / cl, ny = cx / cl;
        float maxAbs = 0;
        SKPoint bow = start;
        int pos = 0, neg = 0;
        for (int i = 1; i < tipI; i++)
        {
            float perp = (pts[i].X - start.X) * nx + (pts[i].Y - start.Y) * ny;
            if (perp > 6f) pos++;
            else if (perp < -6f) neg++;
            if (MathF.Abs(perp) > maxAbs)
            {
                maxAbs = MathF.Abs(perp);
                bow = pts[i];
            }
        }

        if (maxAbs >= 14f && (pos == 0 || neg == 0))
        {
            var curved = new ArrowItem { Start = start, End = tip, BendPoint = bow, HeadScale = 1f };
            CopyStyle(curved, style);
            curved.SyncBounds();
            return curved;
        }

        if (maxAbs < 14f)
        {
            var straight = new ArrowItem { Start = start, End = tip, HeadScale = 1f };
            CopyStyle(straight, style);
            straight.SyncBounds();
            return straight;
        }

        return PolyArrow(pts, tipI, style);
    }

    private static StrokeArrowItem PolyArrow(IReadOnlyList<SKPoint> pts, int tipI, SceneItem style)
    {
        var shaft = new List<SKPoint>(tipI + 1);
        for (int i = 0; i <= tipI; i++)
            shaft.Add(pts[i]);
        var arrow = new StrokeArrowItem { Points = shaft, HeadScale = 1f };
        CopyStyle(arrow, style);
        arrow.SyncBounds();
        return arrow;
    }

    private static int FindApex(IReadOnlyList<SKPoint> pts)
    {
        float total = PathLength(pts);
        if (total < 1f) return -1;
        var mid = PointAt(pts, total * 0.62f);
        float dx = mid.X - pts[0].X, dy = mid.Y - pts[0].Y;
        float dl = MathF.Sqrt(dx * dx + dy * dy);
        if (dl < 8f) return -1;
        dx /= dl;
        dy /= dl;

        int best = -1;
        float bestProj = float.MinValue;
        float acc = 0;
        for (int t = 1; t < pts.Count - 1; t++)
        {
            acc += SKPoint.Distance(pts[t - 1], pts[t]);
            if (acc < total * 0.55f || acc > total * 0.94f) continue;
            if (!ShaftDir(pts, t, out float sx, out float sy)) continue;
            if (!HeadOk(pts, t, sx, sy, twoStroke: false)) continue;
            float proj = (pts[t].X - pts[0].X) * dx + (pts[t].Y - pts[0].Y) * dy;
            if (proj > bestProj + 2f)
            {
                bestProj = proj;
                best = t;
            }
        }
        if (best >= 0) return best;
        var late = PointAt(pts, total * 0.82f);
        for (int t = pts.Count - 2; t >= 2; t--)
        {
            if (SKPoint.Distance(pts[t], late) > 40f && t < pts.Count * 3 / 4) continue;
            if (!ShaftDir(pts, t, out float sx, out float sy)) continue;
            if (HeadOk(pts, t, sx, sy, twoStroke: false))
                return t;
        }
        return -1;
    }

    private static int TrimBarb(IReadOnlyList<SKPoint> pts, int tipI, float sx, float sy)
    {
        while (tipI > 2)
        {
            float vx = pts[tipI].X - pts[tipI - 1].X;
            float vy = pts[tipI].Y - pts[tipI - 1].Y;
            float d = MathF.Sqrt(vx * vx + vy * vy);
            if (d < 2f) { tipI--; continue; }
            float along = (vx / d) * sx + (vy / d) * sy;
            if (along < 0.55f) tipI--;
            else break;
        }
        return tipI;
    }

    private static bool ShaftDir(IReadOnlyList<SKPoint> pts, int tipI, out float sx, out float sy)
    {
        sx = sy = 0;
        float need = 36f;
        float acc = 0;
        var tip = pts[tipI];
        for (int i = tipI; i > 0; i--)
        {
            acc += SKPoint.Distance(pts[i], pts[i - 1]);
            if (acc >= need || i == 1)
            {
                sx = tip.X - pts[i - 1].X;
                sy = tip.Y - pts[i - 1].Y;
                float sl = MathF.Sqrt(sx * sx + sy * sy);
                if (sl < 4f) return false;
                sx /= sl;
                sy /= sl;
                return true;
            }
        }
        return false;
    }

    private static SKPoint PointAt(IReadOnlyList<SKPoint> pts, float dist)
    {
        float acc = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            float d = SKPoint.Distance(pts[i - 1], pts[i]);
            if (acc + d >= dist) return pts[i];
            acc += d;
        }
        return pts[^1];
    }

    private static void CopyStyle(SceneItem dest, SceneItem src)
    {
        dest.StrokeColor = src.StrokeColor;
        dest.StrokeWidth = src.StrokeWidth;
        dest.FillColor = SKColors.Transparent;
        dest.Opacity = 1f;
    }

    private static float PathLength(IReadOnlyList<SKPoint> pts, int from = 0, int to = -1)
    {
        if (to < 0) to = pts.Count - 1;
        float n = 0;
        for (int i = from; i < to; i++)
            n += SKPoint.Distance(pts[i], pts[i + 1]);
        return n;
    }
}
