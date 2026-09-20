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

        if (IsScribble(pts)) return null;

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

    public static SceneItem? TryFitAnyShape(IReadOnlyList<SKPoint> pts, SceneItem style, IReadOnlyList<SKPoint>? extra = null)
    {
        if (pts.Count < 3) return null;

        if (extra is { Count: >= 2 } && pts.Count >= 3
            && PathLength(extra) <= PathLength(pts) * 0.7f
            && HeadNearTip(pts, extra)
            && TryArrow(Merge(pts, extra), style, tipAt: pts.Count - 1) is { } paired)
            return paired;

        float len = PathLength(pts);
        if (len < 36f) return null;

        if (!IsClosed(pts, len) && TryArrow(pts, style) is { } arrow)
            return arrow;

        if (IsScribble(pts)) return null;

        if (IsClosed(pts, len))
        {
            if (TryFitRectangle(pts, style) is { } rect)
                return rect;
            if (TryFitCircleOrEllipse(pts, style) is { } circle)
                return circle;
            if (TryFitTriangle(pts, style) is { } tri)
                return tri;
            return null;
        }

        if (TryFitLine(pts, style) is { } line)
            return line;

        return null;
    }

    public static bool IsScribble(IReadOnlyList<SKPoint> pts)
    {
        if (pts.Count < 5) return false;
        float len = PathLength(pts);
        if (len < 35f) return false;

        float chord = SKPoint.Distance(pts[0], pts[^1]);
        if (chord >= 40f && chord / len >= 0.86f)
            return false;

        GetBounds(pts, out float minX, out float maxX, out float minY, out float maxY);
        float w = maxX - minX, h = maxY - minY;
        if (Math.Max(w, h) < 14f) return true;

        float boxPerimeter = 2f * (w + h);
        if (boxPerimeter > 10f && len > boxPerimeter * 1.55f)
            return true;

        int revX = CountAxisReversals(pts, true);
        int revY = CountAxisReversals(pts, false);
        if (revX >= 3 && revY >= 3)
            return true;

        if (CountSelfIntersections(pts) >= 3)
            return true;

        return false;
    }

    public static LineItem? TryFitLine(IReadOnlyList<SKPoint> pts, SceneItem style)
    {
        if (pts.Count < 3) return null;
        float len = PathLength(pts);
        if (len < 36f) return null;
        if (IsClosed(pts, len)) return null;

        float chord = SKPoint.Distance(pts[0], pts[^1]);
        if (chord < 32f || chord / len < 0.88f) return null;

        float maxDev = MaxPerpendicularDistance(pts, pts[0], pts[^1]);
        if (maxDev > Math.Max(7f, chord * 0.075f)) return null;

        var line = new LineItem { Start = pts[0], End = pts[^1] };
        CopyStyle(line, style);
        line.SyncBounds();
        return line;
    }

    public static EllipseItem? TryFitCircleOrEllipse(IReadOnlyList<SKPoint> pts, SceneItem style)
    {
        if (pts.Count < 6) return null;
        float len = PathLength(pts);
        if (len < 45f) return null;
        if (!IsClosed(pts, len)) return null;

        GetBounds(pts, out float minX, out float maxX, out float minY, out float maxY);
        float w = maxX - minX, h = maxY - minY;
        if (w < 20f || h < 20f) return null;

        float perimeter = 2f * (w + h);
        if (len < perimeter * 0.55f || len > perimeter * 1.48f) return null;

        var center = new SKPoint((minX + maxX) / 2f, (minY + maxY) / 2f);
        float meanR = 0f;
        for (int i = 0; i < pts.Count; i++)
            meanR += SKPoint.Distance(pts[i], center);
        meanR /= pts.Count;
        if (meanR < 8f) return null;

        float varSum = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            float diff = SKPoint.Distance(pts[i], center) - meanR;
            varSum += diff * diff;
        }
        float stdev = MathF.Sqrt(varSum / pts.Count);
        float relVar = stdev / meanR;

        float ratio = w > h ? w / h : h / w;
        if (ratio <= 1.25f && relVar <= 0.26f)
        {
            float r = (w + h) / 4f;
            var circle = new EllipseItem
            {
                Bounds = new SKRect(center.X - r, center.Y - r, center.X + r, center.Y + r),
                Rotation = 0,
            };
            CopyStyle(circle, style);
            return circle;
        }

        if (ratio <= 2.8f)
        {
            float a = w / 2f, b = h / 2f;
            float ellVarSum = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                float nx = (pts[i].X - center.X) / a;
                float ny = (pts[i].Y - center.Y) / b;
                float distNorm = MathF.Sqrt(nx * nx + ny * ny);
                float diff = distNorm - 1f;
                ellVarSum += diff * diff;
            }
            float ellRms = MathF.Sqrt(ellVarSum / pts.Count);
            if (ellRms <= 0.20f)
            {
                var ell = new EllipseItem
                {
                    Bounds = new SKRect(minX, minY, maxX, maxY),
                    Rotation = 0,
                };
                CopyStyle(ell, style);
                return ell;
            }
        }

        return null;
    }

    public static RectItem? TryFitRectangle(IReadOnlyList<SKPoint> pts, SceneItem style)
    {
        if (pts.Count < 6) return null;
        float len = PathLength(pts);
        if (len < 50f) return null;
        if (!IsClosed(pts, len)) return null;

        GetBounds(pts, out float minX, out float maxX, out float minY, out float maxY);
        float w = maxX - minX, h = maxY - minY;
        if (w < 20f || h < 20f) return null;

        float perimeter = 2f * (w + h);
        if (len < perimeter * 0.70f || len > perimeter * 1.50f) return null;

        float diag = MathF.Sqrt(w * w + h * h);
        var poly = DouglasPeucker(pts, Math.Max(7f, diag * 0.05f));
        if (poly.Count is < 4 or > 6) return null;

        float polyArea = MathF.Abs(PolygonArea(poly));
        if (polyArea < w * h * 0.68f) return null;

        SKRect bounds = new(minX, minY, maxX, maxY);
        float ratio = w > h ? w / h : h / w;
        if (ratio <= 1.15f)
        {
            float side = (w + h) / 2f;
            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;
            bounds = new SKRect(cx - side / 2f, cy - side / 2f, cx + side / 2f, cy + side / 2f);
        }

        var rect = new RectItem
        {
            Bounds = bounds,
            CornerRadius = 0,
        };
        CopyStyle(rect, style);
        return rect;
    }

    public static InkPolygonItem? TryFitTriangle(IReadOnlyList<SKPoint> pts, SceneItem style)
    {
        if (pts.Count < 6) return null;
        float len = PathLength(pts);
        if (len < 50f) return null;
        if (!IsClosed(pts, len)) return null;

        GetBounds(pts, out float minX, out float maxX, out float minY, out float maxY);
        float w = maxX - minX, h = maxY - minY;
        if (w < 18f || h < 18f) return null;

        float diag = MathF.Sqrt(w * w + h * h);
        var poly = DouglasPeucker(pts, Math.Max(9f, diag * 0.065f));
        if (poly.Count is < 3 or > 5) return null;

        SKPoint[] tri;
        if (poly.Count >= 4 && SKPoint.Distance(poly[0], poly[^1]) <= diag * 0.20f)
            tri = [poly[0], poly[1], poly[2]];
        else if (poly.Count == 3)
            tri = [poly[0], poly[1], poly[2]];
        else
            return null;

        float area = MathF.Abs(PolygonArea(tri));
        if (area < w * h * 0.18f) return null;

        var polyItem = new InkPolygonItem
        {
            Vertices = tri,
        };
        CopyStyle(polyItem, style);
        polyItem.SyncBounds();
        return polyItem;
    }

    public static List<SKPoint> DouglasPeucker(IReadOnlyList<SKPoint> pts, float epsilon)
    {
        if (pts.Count < 3) return new List<SKPoint>(pts);
        var result = new List<SKPoint>();
        bool[] keep = new bool[pts.Count];
        keep[0] = true;
        keep[^1] = true;
        DPHelper(pts, 0, pts.Count - 1, epsilon, keep);
        for (int i = 0; i < pts.Count; i++)
        {
            if (keep[i]) result.Add(pts[i]);
        }
        return result;
    }

    private static void DPHelper(IReadOnlyList<SKPoint> pts, int start, int end, float epsilon, bool[] keep)
    {
        if (end <= start + 1) return;
        float maxDist = 0f;
        int maxIdx = start;
        float aX = pts[start].X, aY = pts[start].Y;
        float bX = pts[end].X, bY = pts[end].Y;
        float dx = bX - aX, dy = bY - aY;
        float lineLen = MathF.Sqrt(dx * dx + dy * dy);

        for (int i = start + 1; i < end; i++)
        {
            float dist;
            if (lineLen < 1e-4f)
                dist = SKPoint.Distance(pts[i], pts[start]);
            else
                dist = MathF.Abs(dy * pts[i].X - dx * pts[i].Y + bX * aY - bY * aX) / lineLen;

            if (dist > maxDist)
            {
                maxDist = dist;
                maxIdx = i;
            }
        }

        if (maxDist > epsilon)
        {
            keep[maxIdx] = true;
            DPHelper(pts, start, maxIdx, epsilon, keep);
            DPHelper(pts, maxIdx, end, epsilon, keep);
        }
    }

    private static float PolygonArea(IReadOnlyList<SKPoint> poly)
    {
        float area = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            int j = (i + 1) % poly.Count;
            area += poly[i].X * poly[j].Y - poly[j].X * poly[i].Y;
        }
        return area * 0.5f;
    }

    public static void GetBounds(IReadOnlyList<SKPoint> pts, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = maxX = pts[0].X;
        minY = maxY = pts[0].Y;
        for (int i = 1; i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
    }

    private static float MaxPerpendicularDistance(IReadOnlyList<SKPoint> pts, SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 1e-4f) return 0f;
        float max = 0f;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            float dev = MathF.Abs(dy * pts[i].X - dx * pts[i].Y + b.X * a.Y - b.Y * a.X) / d;
            if (dev > max) max = dev;
        }
        return max;
    }

    private static int CountAxisReversals(IReadOnlyList<SKPoint> pts, bool horizontal)
    {
        int reversals = 0;
        float minMove = 14f;
        int lastDir = 0;
        float anchor = horizontal ? pts[0].X : pts[0].Y;

        for (int i = 1; i < pts.Count; i++)
        {
            float val = horizontal ? pts[i].X : pts[i].Y;
            float diff = val - anchor;
            if (MathF.Abs(diff) >= minMove)
            {
                int dir = diff > 0 ? 1 : -1;
                if (lastDir != 0 && dir != lastDir)
                    reversals++;
                lastDir = dir;
                anchor = val;
            }
        }
        return reversals;
    }

    private static int CountSelfIntersections(IReadOnlyList<SKPoint> pts)
    {
        int step = Math.Max(1, pts.Count / 22);
        var sampled = new List<SKPoint>(pts.Count / step + 2);
        for (int i = 0; i < pts.Count; i += step)
            sampled.Add(pts[i]);
        if (sampled[^1] != pts[^1])
            sampled.Add(pts[^1]);

        int count = 0;
        for (int i = 0; i < sampled.Count - 1; i++)
        {
            for (int j = i + 2; j < sampled.Count - 1; j++)
            {
                if (i == 0 && j == sampled.Count - 2) continue;
                if (SegmentsIntersect(sampled[i], sampled[i + 1], sampled[j], sampled[j + 1]))
                {
                    count++;
                    if (count >= 3) return count;
                }
            }
        }
        return count;
    }

    private static bool SegmentsIntersect(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
    {
        static float CCW(SKPoint p1, SKPoint p2, SKPoint p3) =>
            (p3.Y - p1.Y) * (p2.X - p1.X) - (p2.Y - p1.Y) * (p3.X - p1.X);

        float ccw1 = CCW(a, b, c);
        float ccw2 = CCW(a, b, d);
        float ccw3 = CCW(c, d, a);
        float ccw4 = CCW(c, d, b);

        return ((ccw1 > 0 && ccw2 < 0) || (ccw1 < 0 && ccw2 > 0))
            && ((ccw3 > 0 && ccw4 < 0) || (ccw3 < 0 && ccw4 > 0));
    }

    private static bool IsClosed(IReadOnlyList<SKPoint> pts, float len) =>
        SKPoint.Distance(pts[0], pts[^1]) <= Math.Max(28f, len * 0.16f);

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
