using System.Numerics;
using System.Windows.Threading;
using SkiaSharp;
using ScreenForge.Editor;
using Windows.UI.Input.Inking;
using Windows.UI.Input.Inking.Analysis;
using WinPoint = global::Windows.Foundation.Point;
using WinRect = global::Windows.Foundation.Rect;

namespace ScreenForge.Presenter;

internal sealed class InkBeautifier
{
    private readonly PresenterRenderer _renderer;
    private readonly InkAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<uint, FreehandItem> _map = [];
    private bool _busy;

    public event Action? Applied;

    public InkBeautifier(PresenterRenderer renderer)
    {
        _renderer = renderer;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => _ = AnalyzeAsync();
    }

    public void Pause() => _timer.Stop();

    public void Enqueue(FreehandItem item)
    {
        if (item.Points.Count < 2) return;
        try
        {
            var stroke = BuildStroke(item);
            _analyzer.AddDataForStroke(stroke);
            _map[stroke.Id] = item;
            _timer.Stop();
            _timer.Start();
        }
        catch
        {
        }
    }

    public void Reset()
    {
        _timer.Stop();
        try { _analyzer.ClearDataForAllStrokes(); } catch { }
        _map.Clear();
        _busy = false;
    }

    private static InkStroke BuildStroke(FreehandItem item)
    {
        var builder = new InkStrokeBuilder();
        var pts = item.Points;
        int step = pts.Count > 240 ? pts.Count / 180 : 1;
        var ink = new List<InkPoint>(pts.Count / step + 1);
        for (int i = 0; i < pts.Count; i += step)
            ink.Add(new InkPoint(new WinPoint(pts[i].X, pts[i].Y), 0.5f));
        if ((pts.Count - 1) % step != 0)
            ink.Add(new InkPoint(new WinPoint(pts[^1].X, pts[^1].Y), 0.5f));
        return builder.CreateStrokeFromInkPoints(ink, Matrix3x2.Identity);
    }

    private async Task AnalyzeAsync()
    {
        _timer.Stop();
        if (_busy || _map.Count == 0 || _renderer.IsDrawing) return;
        _busy = true;
        try
        {
            var result = await _analyzer.AnalyzeAsync();
            bool changed = false;
            if (result.Status == InkAnalysisStatus.Updated)
                changed |= ConvertWords();
            changed |= ConvertLinesAndArrows();
            if (result.Status == InkAnalysisStatus.Updated)
                changed |= ConvertDrawings();
            if (changed)
                Applied?.Invoke();
        }
        catch
        {
            if (ConvertLinesAndArrows())
                Applied?.Invoke();
        }
        finally
        {
            _busy = false;
        }
    }

    private bool ConvertLinesAndArrows()
    {
        bool changed = false;
        var pending = _map.ToArray();
        var used = new HashSet<uint>();
        for (int i = 0; i < pending.Length; i++)
        {
            if (used.Contains(pending[i].Key)) continue;
            if (i + 1 < pending.Length && !used.Contains(pending[i + 1].Key))
            {
                var a = pending[i].Value;
                var b = pending[i + 1].Value;
                var pair = InkStrokeFit.TryFit(a.Points, a, b.Points);
                if (pair is ArrowItem or StrokeArrowItem)
                {
                    _renderer.Replace([a, b], pair);
                    Forget([pending[i].Key, pending[i + 1].Key]);
                    used.Add(pending[i].Key);
                    used.Add(pending[i + 1].Key);
                    changed = true;
                    continue;
                }
            }

            var fitted = InkStrokeFit.TryFit(pending[i].Value.Points, pending[i].Value);
            if (fitted is not (ArrowItem or StrokeArrowItem or LineItem)) continue;
            _renderer.Replace([pending[i].Value], fitted);
            Forget([pending[i].Key]);
            used.Add(pending[i].Key);
            changed = true;
        }
        return changed;
    }

    private bool ConvertDrawings()
    {
        bool changed = false;
        var nodes = _analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.InkDrawing);
        foreach (var node in nodes)
        {
            if (node is not InkAnalysisInkDrawing shape) continue;
            if (shape.DrawingKind == InkAnalysisDrawingKind.Drawing) continue;
            var items = MapItems(shape.GetStrokeIds());
            if (items.Count == 0) continue;
            double w = shape.BoundingRect.Width, h = shape.BoundingRect.Height;
            if (Math.Max(w, h) < 40) continue;
            if (shape.DrawingKind is InkAnalysisDrawingKind.Circle or InkAnalysisDrawingKind.Ellipse
                && Math.Min(w, h) < 28) continue;
            if (!LooksClosed(items)) continue;
            var replacement = ToItem(shape, items[0]);
            if (replacement == null) continue;
            _renderer.Replace(items, replacement);
            Forget(shape.GetStrokeIds());
            changed = true;
        }
        return changed;
    }

    private bool ConvertWords()
    {
        bool changed = false;
        var nodes = _analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.InkWord);
        foreach (var node in nodes)
        {
            if (node is not InkAnalysisInkWord word) continue;
            if (string.IsNullOrWhiteSpace(word.RecognizedText)) continue;
            var items = MapItems(word.GetStrokeIds());
            if (items.Count == 0) continue;
            var r = word.BoundingRect;
            if (IsJunkWord(word.RecognizedText, r, items)) continue;
            var src = items[0];
            var text = new TextItem
            {
                Text = word.RecognizedText.Trim(),
                FontFamily = "Segoe UI",
                FontSize = Math.Clamp((float)r.Height * 0.92f, 14f, 160f),
                Bold = true,
                Shadow = false,
                Ribbon = false,
                Position = new SKPoint((float)r.X, (float)r.Y),
                StrokeColor = src.StrokeColor,
                StrokeWidth = src.StrokeWidth,
                FillColor = SKColors.Transparent,
                Opacity = 1f,
            };
            text.Measure();
            _renderer.Replace(items, text);
            Forget(word.GetStrokeIds());
            changed = true;
        }
        return changed;
    }

    private static bool LooksClosed(List<SceneItem> items)
    {
        if (items.Count != 1 || items[0] is not FreehandItem fh || fh.Points.Count < 4)
            return false;
        float len = 0;
        for (int i = 1; i < fh.Points.Count; i++)
            len += SKPoint.Distance(fh.Points[i - 1], fh.Points[i]);
        return SKPoint.Distance(fh.Points[0], fh.Points[^1]) <= Math.Max(28f, len * 0.15f);
    }

    private static bool IsJunkWord(string raw, WinRect r, List<SceneItem> items)
    {
        var t = raw.Trim();
        if (t.Length == 0) return true;
        if (r.Height < 28 || Math.Max(r.Width, r.Height) < 36) return true;
        if (t.Length == 1 && !char.IsLetterOrDigit(t[0])) return true;
        if (items.Count == 1 && items[0] is FreehandItem fh && LooksLinear(fh.Points)
            && SKPoint.Distance(fh.Points[0], fh.Points[^1]) >= 40f)
            return true;
        if (t.Length == 1 && r.Height < 40) return true;
        return false;
    }

    private static bool LooksLinear(List<SKPoint> pts)
    {
        if (pts.Count < 2) return true;
        float chord = SKPoint.Distance(pts[0], pts[^1]);
        float len = 0;
        for (int i = 1; i < pts.Count; i++)
            len += SKPoint.Distance(pts[i - 1], pts[i]);
        return len > 1 && chord / len >= 0.82f;
    }

    private List<SceneItem> MapItems(IReadOnlyList<uint> ids)
    {
        var list = new List<SceneItem>(ids.Count);
        foreach (var id in ids)
        {
            if (_map.TryGetValue(id, out var item))
                list.Add(item);
        }
        return list;
    }

    private void Forget(IReadOnlyList<uint> ids)
    {
        try { _analyzer.RemoveDataForStrokes(ids); } catch { }
        foreach (var id in ids)
            _map.Remove(id);
    }

    private static SceneItem? ToItem(InkAnalysisInkDrawing shape, SceneItem style)
    {
        var kind = shape.DrawingKind;
        if (kind is InkAnalysisDrawingKind.Circle or InkAnalysisDrawingKind.Ellipse)
            return ToEllipse(shape, style);

        if (kind is InkAnalysisDrawingKind.Rectangle or InkAnalysisDrawingKind.Square)
        {
            var r = shape.BoundingRect;
            var rect = new RectItem
            {
                Bounds = new SKRect((float)r.X, (float)r.Y, (float)(r.X + r.Width), (float)(r.Y + r.Height)),
                CornerRadius = 0,
                StrokeColor = style.StrokeColor,
                StrokeWidth = style.StrokeWidth,
                FillColor = SKColors.Transparent,
                Opacity = 1f,
            };
            return rect;
        }

        var pts = shape.Points;
        if (pts.Count < 2) return null;
        var poly = new InkPolygonItem
        {
            Vertices = pts.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToArray(),
            StrokeColor = style.StrokeColor,
            StrokeWidth = style.StrokeWidth,
            FillColor = SKColors.Transparent,
            Opacity = 1f,
        };
        poly.SyncBounds();
        return poly;
    }

    private static EllipseItem ToEllipse(InkAnalysisInkDrawing shape, SceneItem style)
    {
        var pts = shape.Points;
        SKRect bounds;
        float rotation = 0;
        if (pts.Count >= 4)
        {
            var c = new SKPoint(
                (float)((pts[0].X + pts[2].X) / 2.0),
                (float)((pts[0].Y + pts[2].Y) / 2.0));
            float w = Dist(pts[0], pts[2]);
            float h = shape.DrawingKind == InkAnalysisDrawingKind.Circle ? w : Dist(pts[1], pts[3]);
            bounds = new SKRect(c.X - w / 2f, c.Y - h / 2f, c.X + w / 2f, c.Y + h / 2f);
            if (shape.DrawingKind != InkAnalysisDrawingKind.Circle)
                rotation = (float)(Math.Atan2(pts[2].Y - pts[0].Y, pts[2].X - pts[0].X) * 180.0 / Math.PI);
        }
        else
        {
            var r = shape.BoundingRect;
            bounds = new SKRect((float)r.X, (float)r.Y, (float)(r.X + r.Width), (float)(r.Y + r.Height));
        }
        return new EllipseItem
        {
            Bounds = bounds,
            Rotation = rotation,
            StrokeColor = style.StrokeColor,
            StrokeWidth = style.StrokeWidth,
            FillColor = SKColors.Transparent,
            Opacity = 1f,
        };
    }

    private static float Dist(WinPoint a, WinPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}

internal sealed class InkPolygonItem : SceneItem
{
    public SKPoint[] Vertices { get; set; } = [];

    public void SyncBounds()
    {
        if (Vertices.Length == 0) { Bounds = SKRect.Empty; return; }
        float minX = Vertices[0].X, minY = Vertices[0].Y, maxX = minX, maxY = minY;
        foreach (var p in Vertices)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        Bounds = new SKRect(minX, minY, maxX, maxY);
    }

    public override void Move(float dx, float dy)
    {
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i] = new SKPoint(Vertices[i].X + dx, Vertices[i].Y + dy);
        SyncBounds();
    }

    public override void Render(SKCanvas canvas)
    {
        if (Vertices.Length < 2) return;
        canvas.Save();
        ApplyRotation(canvas);
        using var path = new SKPath();
        path.MoveTo(Vertices[0]);
        for (int i = 1; i < Vertices.Length; i++)
            path.LineTo(Vertices[i]);
        path.Close();
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = StrokeColor.WithAlpha(AlphaByte),
            StrokeWidth = StrokeWidth,
            IsAntialias = true,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    public override SceneItem Clone()
    {
        var c = new InkPolygonItem { Vertices = (SKPoint[])Vertices.Clone() };
        CopyBaseTo(c);
        return c;
    }
}
