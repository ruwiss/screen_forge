using System.Numerics;
using System.Windows.Threading;
using SkiaSharp;
using ScreenForge.Editor;
using ScreenForge.Settings;
using Windows.UI.Input.Inking;
using Windows.UI.Input.Inking.Analysis;
using WinPoint = global::Windows.Foundation.Point;
using WinRect = global::Windows.Foundation.Rect;

namespace ScreenForge.Presenter;

internal sealed class InkBeautifier
{
    private readonly PresenterRenderer _renderer;
    private readonly Func<PresenterSettings> _settings;
    private readonly InkAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<uint, FreehandItem> _map = [];
    private bool _busy;

    public event Action? Applied;

    public InkBeautifier(PresenterRenderer renderer, Func<PresenterSettings> settings)
    {
        _renderer = renderer;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => _ = AnalyzeAsync();
    }

    public void Pause() => _timer.Stop();

    public void Enqueue(FreehandItem item)
    {
        if (item.Points.Count == 0) return;
        if (item.Points.Count > 1 && InkStrokeFit.IsScribble(item.Points)) return;

        try
        {
            var stroke = BuildStroke(item);
            _analyzer.AddDataForStroke(stroke);
            _map[stroke.Id] = item;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(850);
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
        if (pts.Count == 1)
        {
            var p = pts[0];
            var dotPts = new List<InkPoint>
            {
                new(new WinPoint(p.X, p.Y), 0.5f),
                new(new WinPoint(p.X + 0.1, p.Y + 0.1), 0.5f)
            };
            return builder.CreateStrokeFromInkPoints(dotPts, Matrix3x2.Identity);
        }

        int step = pts.Count > 600 ? pts.Count / 300 : 1;
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
        if (_busy || _map.Count == 0) return;
        if (_renderer.IsDrawing)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Start();
            return;
        }

        _busy = true;
        try
        {
            var cfg = _settings();
            var result = await _analyzer.AnalyzeAsync();
            bool changed = false;

            // 1. Text handwriting recognition (if enabled)
            if (cfg.InkTextConvertEnabled && result.Status == InkAnalysisStatus.Updated)
                changed |= ConvertText();

            // 2. Shape recognition (if Auto mode)
            if (cfg.InkShapeConvert == InkShapeConvertMode.Auto)
            {
                changed |= ConvertLinesAndArrows();
                if (result.Status == InkAnalysisStatus.Updated)
                    changed |= ConvertDrawings();
            }

            if (changed)
                Applied?.Invoke();
        }
        catch
        {
            if (_settings().InkShapeConvert == InkShapeConvertMode.Auto && ConvertLinesAndArrows())
                Applied?.Invoke();
        }
        finally
        {
            // CRITICAL: Any strokes not converted during this window are evicted from analyzer.
            // This guarantees previous unconverted drawings remain permanently as freehand ink
            // and are NEVER merged with or deleted by future strokes!
            if (_map.Count > 0)
            {
                var remaining = _map.Keys.ToArray();
                Forget(remaining);
            }
            _busy = false;
        }
    }

    private static string? s_handwritingFont;

    public static string ResolveHandwritingFont()
    {
        if (s_handwritingFont != null) return s_handwritingFont;

        string[] candidates = ["Ink Free", "Segoe Print", "Segoe Script", "Comic Sans MS"];
        foreach (var font in candidates)
        {
            using var tf = SKTypeface.FromFamilyName(font);
            if (tf != null && string.Equals(tf.FamilyName, font, StringComparison.OrdinalIgnoreCase))
            {
                s_handwritingFont = font;
                return font;
            }
        }

        s_handwritingFont = "Segoe UI";
        return s_handwritingFont;
    }

    private bool ConvertText()
    {
        bool changed = false;
        var font = ResolveHandwritingFont();

        // 1. Try Line level first (keeps multi-word phrases and sentences together with natural spacing)
        var lines = _analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.Line);
        foreach (var node in lines)
        {
            if (node is not InkAnalysisLine line) continue;
            if (string.IsNullOrWhiteSpace(line.RecognizedText)) continue;
            var items = MapItems(line.GetStrokeIds());
            if (items.Count == 0) continue;
            var r = line.BoundingRect;
            if (IsJunkText(line.RecognizedText, r, items)) continue;

            var src = items[0];
            var text = new TextItem
            {
                Text = line.RecognizedText.Trim(),
                FontFamily = font,
                FontSize = Math.Clamp((float)r.Height * 0.88f, 14f, 160f),
                Bold = false,
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
            Forget(line.GetStrokeIds());
            changed = true;
        }

        // 2. Fallback to InkWord for any strokes not grouped into a line
        var words = _analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.InkWord);
        foreach (var node in words)
        {
            if (node is not InkAnalysisInkWord word) continue;
            if (string.IsNullOrWhiteSpace(word.RecognizedText)) continue;
            var items = MapItems(word.GetStrokeIds());
            if (items.Count == 0) continue;
            var r = word.BoundingRect;
            if (IsJunkText(word.RecognizedText, r, items)) continue;

            var src = items[0];
            var text = new TextItem
            {
                Text = word.RecognizedText.Trim(),
                FontFamily = font,
                FontSize = Math.Clamp((float)r.Height * 0.88f, 14f, 160f),
                Bold = false,
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

    private static bool IsJunkText(string raw, WinRect r, List<SceneItem> items)
    {
        var t = raw.Trim();
        if (t.Length == 0) return true;
        if (r.Height < 12 || Math.Max(r.Width, r.Height) < 14) return true;

        // Any stroke that is a scribble is not a word
        foreach (var item in items)
        {
            if (item is FreehandItem fh && InkStrokeFit.IsScribble(fh.Points))
                return true;
        }

        // Single character validation
        if (t.Length == 1)
        {
            char c = t[0];
            if (!char.IsLetterOrDigit(c)) return true;
            if (r.Height < 16) return true;
            if (items.Count == 1 && items[0] is FreehandItem fh && LooksLinear(fh.Points)
                && SKPoint.Distance(fh.Points[0], fh.Points[^1]) >= 32f)
                return true;
        }

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

    private bool ConvertLinesAndArrows()
    {
        bool changed = false;
        var pending = _map.ToArray();
        var used = new HashSet<uint>();

        for (int i = 0; i < pending.Length; i++)
        {
            if (used.Contains(pending[i].Key)) continue;

            // Two-stroke arrow: shaft followed immediately by tip head
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

            // Single stroke: Try any geometric shape (Line, Arrow, Circle, Ellipse, Rectangle, Triangle)
            var fitted = InkStrokeFit.TryFitAnyShape(pending[i].Value.Points, pending[i].Value);
            if (fitted == null) continue;

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
            if (items.Count != 1) continue; // Only single-stroke closed shapes to avoid grouping unrelated strokes
            if (items[0] is not FreehandItem fh || InkStrokeFit.IsScribble(fh.Points)) continue;

            double w = shape.BoundingRect.Width, h = shape.BoundingRect.Height;
            if (Math.Max(w, h) < 30) continue;
            if (shape.DrawingKind is InkAnalysisDrawingKind.Circle or InkAnalysisDrawingKind.Ellipse
                && Math.Min(w, h) < 22) continue;
            if (!LooksClosed(items)) continue;

            var replacement = ToItem(shape, items[0]);
            if (replacement == null) continue;

            _renderer.Replace(items, replacement);
            Forget(shape.GetStrokeIds());
            changed = true;
        }
        return changed;
    }

    private static bool LooksClosed(List<SceneItem> items)
    {
        if (items.Count != 1 || items[0] is not FreehandItem fh || fh.Points.Count < 4)
            return false;
        if (InkStrokeFit.IsScribble(fh.Points)) return false;

        float len = 0;
        for (int i = 1; i < fh.Points.Count; i++)
            len += SKPoint.Distance(fh.Points[i - 1], fh.Points[i]);
        return SKPoint.Distance(fh.Points[0], fh.Points[^1]) <= Math.Max(28f, len * 0.16f);
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
        if (pts.Count < 3) return null;
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

public sealed class InkPolygonItem : SceneItem
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
