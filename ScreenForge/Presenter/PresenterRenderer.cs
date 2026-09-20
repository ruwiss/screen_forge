using SkiaSharp;
using ScreenForge.Editor;
using ScreenForge.Settings;

namespace ScreenForge.Presenter;

public sealed class PresenterRenderer
{
    private readonly List<SceneItem> _ink = [];
    private readonly List<LaserTrail> _lasers = [];
    private LaserTrail? _liveLaser;
    private SceneItem? _draft;
    private SKPoint _start;
    private bool _arrowFlipped;
    private readonly SKPaint _laserStroke = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };
    private readonly SKPath _laserPath = new();
    private readonly SKPaint _spotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public PresenterTool Tool { get; set; }
    public bool Spotlight { get; set; }
    public float SpotlightAmount { get; set; }
    public SKPoint Cursor { get; set; }
    public SKColor StrokeColor { get; set; } = new(0xEA, 0x6F, 0x12);
    public float StrokeWidth { get; set; } = 5f;
    public SKRect ViewBounds { get; set; } = new(0, 0, 1920, 1080);

    public bool HasInk => _ink.Count > 0 || _draft != null;
    public bool HasLaser => _liveLaser != null || _lasers.Exists(l => !l.IsEmpty);
    public bool IsDrawing => _draft != null || _liveLaser != null;
    public ArrowItem? DraftArrow => _draft as ArrowItem;
    public FreehandItem? LastFreehand => _ink.Count > 0 ? _ink[^1] as FreehandItem : null;
    public bool BendHeld { get; set; }

    public void Replace(IReadOnlyList<SceneItem> remove, SceneItem add)
    {
        if (remove.Count == 0) return;
        for (int i = _ink.Count - 1; i >= 0; i--)
        {
            for (int j = 0; j < remove.Count; j++)
            {
                if (ReferenceEquals(_ink[i], remove[j]))
                {
                    _ink.RemoveAt(i);
                    break;
                }
            }
        }
        _ink.Add(add);
    }

    public void Clear()
    {
        _ink.Clear();
        _lasers.Clear();
        _liveLaser = null;
        _draft = null;
        _arrowFlipped = false;
    }

    public bool Undo()
    {
        if (_draft != null || _liveLaser != null)
        {
            _draft = null;
            _liveLaser = null;
            _arrowFlipped = false;
            return true;
        }
        if (_ink.Count > 0)
        {
            _ink.RemoveAt(_ink.Count - 1);
            return true;
        }
        if (_lasers.Count > 0)
        {
            _lasers.RemoveAt(_lasers.Count - 1);
            return true;
        }
        return false;
    }

    public void Begin(SKPoint p, PresenterSettings settings)
    {
        _start = p;
        _arrowFlipped = false;
        StrokeWidth = (float)settings.PenWidth;
        switch (Tool)
        {
            case PresenterTool.Pen:
                var pen = Styled(new FreehandItem(), settings);
                pen.AddPoint(p);
                _draft = pen;
                break;
            case PresenterTool.Rectangle:
                _draft = Styled(new RectItem { Bounds = new SKRect(p.X, p.Y, p.X, p.Y), CornerRadius = 12 }, settings);
                break;
            case PresenterTool.Ellipse:
                _draft = Styled(new EllipseItem { Bounds = new SKRect(p.X, p.Y, p.X, p.Y) }, settings);
                break;
            case PresenterTool.Line:
                _draft = Styled(new LineItem { Start = p, End = p }, settings);
                break;
            case PresenterTool.Arrow:
                _draft = Styled(new ArrowItem { Start = p, End = p }, settings);
                break;
            case PresenterTool.Laser:
                _liveLaser = new LaserTrail();
                _liveLaser.Add(p);
                break;
        }
    }

    public void Move(SKPoint p, PresenterSettings settings)
    {
        Cursor = p;
        switch (_draft)
        {
            case FreehandItem pen:
                pen.AddPoint(p);
                break;
            case RectItem rect:
                rect.Bounds = Norm(_start, p);
                rect.CornerRadius = Corner(rect.Bounds);
                break;
            case EllipseItem ell:
                ell.Bounds = Norm(_start, p);
                break;
            case ArrowItem arrow:
                arrow.End = p;
                _arrowFlipped = BendHeld;
                if (SKPoint.Distance(arrow.Start, arrow.End) >= 8f)
                    arrow.BendPoint = ArrowBend.ControlPoint(arrow.Start, arrow.End, ViewBounds, _arrowFlipped);
                arrow.SyncBounds();
                break;
            case LineItem line:
                line.End = p;
                line.SyncBounds();
                break;
        }

        if (_liveLaser != null)
            _liveLaser.Add(p);
    }

    public void End()
    {
        if (_draft is FreehandItem { Points.Count: < 2 })
            _draft = null;
        else if (_draft is LineItem line && SKPoint.Distance(line.Start, line.End) < 4f)
            _draft = null;
        else if (_draft is SceneItem item && item is not LineItem and not FreehandItem)
        {
            if (item.Bounds.Width < 3 && item.Bounds.Height < 3)
                _draft = null;
        }

        if (_draft != null)
            _ink.Add(_draft);
        _draft = null;

        if (_liveLaser != null)
        {
            _liveLaser.Release(Environment.TickCount64);
            _lasers.Add(_liveLaser);
            _liveLaser = null;
        }
    }

    public void ApplyArrowBend(SKRect screen)
    {
        if (_draft is not ArrowItem arrow)
            return;
        _arrowFlipped = BendHeld;
        if (SKPoint.Distance(arrow.Start, arrow.End) >= 8f)
            arrow.BendPoint = ArrowBend.ControlPoint(arrow.Start, arrow.End, screen, _arrowFlipped);
        arrow.SyncBounds();
    }

    public bool Tick(PresenterSettings settings)
    {
        long now = Environment.TickCount64;
        bool dirty = false;
        for (int i = _lasers.Count - 1; i >= 0; i--)
        {
            if (_lasers[i].IsExpired(now, settings.LaserFadeMs) || _lasers[i].IsEmpty)
            {
                _lasers.RemoveAt(i);
                dirty = true;
            }
            else dirty = true;
        }
        if (_liveLaser != null) dirty = true;
        if (Spotlight || SpotlightAmount > 0.01f) dirty = true;
        return dirty;
    }

    public void Render(SKCanvas canvas, int width, int height, PresenterSettings settings)
    {
        ViewBounds = new SKRect(0, 0, width, height);
        canvas.Clear(SKColors.Transparent);

        if (SpotlightAmount > 0.01f)
            DrawSpotlight(canvas, width, height, settings);

        foreach (var item in _ink)
            item.Render(canvas);
        _draft?.Render(canvas);

        var laserColor = Parse(settings.LaserColor, new SKColor(0xFF, 0x3D, 0x6E));
        float laserW = (float)settings.LaserWidth;
        foreach (var trail in _lasers)
            DrawLaser(canvas, trail, laserColor, laserW, fadeMs: settings.LaserFadeMs, live: false);
        if (_liveLaser != null)
            DrawLaser(canvas, _liveLaser, laserColor, laserW, fadeMs: settings.LaserFadeMs, live: true);
    }

    private void DrawSpotlight(SKCanvas canvas, int width, int height, PresenterSettings settings)
    {
        float dim = (float)settings.SpotlightDim * SpotlightAmount;
        float radius = Math.Max(40f, (float)settings.SpotlightRadius);
        float soft = Math.Max(8f, (float)settings.SpotlightSoftness);
        float outer = radius + soft;
        byte a = (byte)Math.Clamp(dim * 255, 0, 220);

        _spotPaint.Shader = null;
        _spotPaint.BlendMode = SKBlendMode.SrcOver;
        _spotPaint.Color = new SKColor(0, 0, 0, a);
        canvas.DrawRect(0, 0, width, height, _spotPaint);

        float inner = radius / outer;
        float mid = inner + (1f - inner) * 0.35f;
        using var punch = SKShader.CreateRadialGradient(
            Cursor,
            outer,
            [SKColors.White, SKColors.White, SKColors.White.WithAlpha(70), SKColors.White.WithAlpha(0)],
            [0f, inner, mid, 1f],
            SKShaderTileMode.Clamp);
        _spotPaint.Shader = punch;
        _spotPaint.Color = SKColors.White;
        _spotPaint.BlendMode = SKBlendMode.DstOut;
        canvas.DrawRect(0, 0, width, height, _spotPaint);
        _spotPaint.Shader = null;
        _spotPaint.BlendMode = SKBlendMode.SrcOver;
        _spotPaint.Color = SKColors.Black;
    }

    private void DrawLaser(SKCanvas canvas, LaserTrail trail, SKColor color, float width, int fadeMs, bool live)
    {
        var pts = trail.Points;
        if (pts.Count == 0) return;
        float w = Math.Max(1.4f, width);
        byte bodyA = color.Alpha < 200 ? (byte)230 : color.Alpha;

        if (pts.Count == 1)
        {
            _laserStroke.StrokeWidth = w * 2.2f;
            _laserStroke.Color = color.WithAlpha(70);
            canvas.DrawCircle(pts[0], 0.4f, _laserStroke);
            _laserStroke.StrokeWidth = w;
            _laserStroke.Color = color.WithAlpha(bodyA);
            canvas.DrawCircle(pts[0], 0.3f, _laserStroke);
            return;
        }

        float life = 0f;
        if (!live && trail.ReleasedAt > 0 && fadeMs > 0)
            life = Math.Clamp((Environment.TickCount64 - trail.ReleasedAt) / (float)fadeMs, 0f, 1f);

        int start = 0;
        int n = pts.Count;
        if (life > 0)
            start = Math.Clamp((int)(life * (n - 1)), 0, n - 2);

        _laserPath.Rewind();
        _laserPath.MoveTo(pts[start]);
        for (int i = start + 1; i < n; i++)
            _laserPath.LineTo(pts[i]);

        byte fade = live ? (byte)70 : (byte)(70 * (1f - life));
        _laserStroke.StrokeWidth = w * 2.4f;
        _laserStroke.Color = color.WithAlpha(fade);
        canvas.DrawPath(_laserPath, _laserStroke);
        _laserStroke.StrokeWidth = w;
        _laserStroke.Color = color.WithAlpha((byte)(bodyA * (live ? 1f : 1f - life * 0.15f)));
        canvas.DrawPath(_laserPath, _laserStroke);
    }

    private T Styled<T>(T item, PresenterSettings settings) where T : SceneItem
    {
        item.StrokeColor = StrokeColor;
        item.StrokeWidth = (float)settings.PenWidth;
        item.FillColor = SKColors.Transparent;
        item.Opacity = 1f;
        return item;
    }

    private static SKRect Norm(SKPoint a, SKPoint b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private static float Corner(SKRect r)
    {
        float m = Math.Min(r.Width, r.Height);
        return Math.Clamp(m * 0.08f, 8f, 20f);
    }

    public static SKColor Parse(string hex, SKColor fallback)
    {
        var c = InteractiveCanvas.ColorFromHex(hex);
        return c.Alpha == 0 && fallback.Alpha != 0 ? fallback : c;
    }

}
