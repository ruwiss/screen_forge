using SkiaSharp;
using ScreenForge.Editor;
using ScreenForge.Presenter;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class PresenterMathTests
{
    [Fact]
    public void ArrowBend_HorizontalPrefersUp()
    {
        var screen = new SKRect(0, 0, 1000, 800);
        var cp = ArrowBend.ControlPoint(new SKPoint(100, 400), new SKPoint(500, 400), screen);
        Assert.True(cp.Y < 400);
        Assert.InRange(cp.X, 280, 320);
    }

    [Fact]
    public void ArrowBend_RightToLeft_StillBowsUp()
    {
        var screen = new SKRect(0, 0, 1000, 800);
        var cp = ArrowBend.ControlPoint(new SKPoint(500, 400), new SKPoint(100, 400), screen);
        Assert.True(cp.Y < 400);
    }

    [Fact]
    public void ArrowBend_Flip_GoesOpposite()
    {
        var screen = new SKRect(0, 0, 1000, 800);
        var a = new SKPoint(100, 400);
        var b = new SKPoint(500, 400);
        var up = ArrowBend.ControlPoint(a, b, screen);
        var down = ArrowBend.ControlPoint(a, b, screen, flip: true);
        Assert.True(up.Y < 400);
        Assert.True(down.Y > 400);
    }

    [Fact]
    public void InkStrokeFit_StraightBecomesLine()
    {
        SKPoint[] pts = [new(0, 0), new(40, 1), new(90, -1), new(140, 0), new(200, 2)];
        var style = new LineItem { StrokeWidth = 5 };
        var fitted = InkStrokeFit.TryFit(pts, style);
        Assert.IsType<LineItem>(fitted);
        Assert.IsNotType<ArrowItem>(fitted);
    }

    [Fact]
    public void InkStrokeFit_HeadAtTipBecomesArrow()
    {
        SKPoint[] pts =
        [
            new(0, 0), new(50, 0), new(100, 0), new(150, 0), new(200, 0),
            new(168, 24), new(200, 0), new(168, -24),
        ];
        var style = new LineItem { StrokeWidth = 5 };
        var arrow = Assert.IsType<ArrowItem>(InkStrokeFit.TryFit(pts, style));
        Assert.InRange(arrow.End.X, 190, 210);
        Assert.Null(arrow.BendPoint);
    }

    [Fact]
    public void InkStrokeFit_BowedShaftKeepsBend()
    {
        SKPoint[] pts =
        [
            new(0, 0), new(50, -28), new(100, -40), new(150, -28), new(200, 0),
            new(168, 24), new(200, 0), new(168, -24),
        ];
        var style = new LineItem { StrokeWidth = 5 };
        var arrow = Assert.IsType<ArrowItem>(InkStrokeFit.TryFit(pts, style));
        Assert.NotNull(arrow.BendPoint);
        Assert.True(arrow.BendPoint.Value.Y < -20);
    }

    [Fact]
    public void InkStrokeFit_ZigZagKeepsBends()
    {
        SKPoint[] pts =
        [
            new(0, 0), new(40, 30), new(80, -30), new(120, 30), new(160, -30), new(200, 0),
            new(165, 20),
        ];
        var arrow = Assert.IsType<StrokeArrowItem>(InkStrokeFit.TryFit(pts, new LineItem { StrokeWidth = 5 }));
        Assert.Contains(arrow.Points, p => p.Y > 20);
        Assert.Contains(arrow.Points, p => p.Y < -20);
        Assert.True(arrow.Points.Count >= 6);
    }

    [Fact]
    public void InkStrokeFit_SingleHookBecomesArrow()
    {
        SKPoint[] pts =
        [
            new(0, 0), new(50, 4), new(110, -3), new(160, 2), new(210, 0),
            new(178, 22),
        ];
        Assert.IsType<ArrowItem>(InkStrokeFit.TryFit(pts, new LineItem { StrokeWidth = 5 }));
    }

    [Fact]
    public void InkStrokeFit_TwoStrokesBecomeArrow()
    {
        SKPoint[] shaft = [new(0, 0), new(60, 2), new(120, -2), new(180, 0), new(220, 1)];
        SKPoint[] head = [new(218, 2), new(188, 28), new(220, 0), new(190, -26)];
        Assert.IsType<ArrowItem>(InkStrokeFit.TryFit(shaft, new LineItem { StrokeWidth = 5 }, head));
    }

    [Fact]
    public void InkStrokeFit_ClosedRectIsNotArrow()
    {
        SKPoint[] pts =
        [
            new(0, 0), new(50, 0), new(100, 0), new(100, 40), new(100, 80),
            new(50, 80), new(0, 80), new(0, 40), new(0, 2),
        ];
        var fitted = InkStrokeFit.TryFit(pts, new LineItem { StrokeWidth = 5 });
        Assert.Null(fitted);
    }

    [Fact]
    public void InkStrokeFit_NoHeadStaysLine()
    {
        SKPoint[] pts = [new(0, 0), new(40, 3), new(90, -2), new(140, 2), new(200, 0)];
        var fitted = InkStrokeFit.TryFit(pts, new LineItem { StrokeWidth = 5 });
        Assert.IsType<LineItem>(fitted);
        Assert.IsNotType<ArrowItem>(fitted);
    }

    [Fact]
    public void InkStrokeFit_ShortStrokeStaysInk()
    {
        SKPoint[] pts = [new(0, 0), new(8, 1), new(16, 0), new(22, 1)];
        Assert.Null(InkStrokeFit.TryFit(pts, new LineItem()));
    }

    [Fact]
    public void LaserTrail_ExpiresAfterFade()
    {
        var trail = new LaserTrail();
        trail.Add(new SKPoint(0, 0));
        trail.Add(new SKPoint(10, 0));
        trail.Release(1000);
        Assert.False(trail.IsExpired(1500, fadeMs: 900));
        Assert.True(trail.IsExpired(2000, fadeMs: 900));
    }

    [Fact]
    public void LaserTrail_TrimsOldestWhenOverMaxLength()
    {
        var trail = new LaserTrail();
        for (int i = 0; i < 600; i++)
            trail.Add(new SKPoint(i * 4, 0));
        Assert.True(trail.Length <= LaserTrail.MaxLength + 4);
        Assert.True(trail.Points[0].X > 0);
    }

    [Fact]
    public void InkStrokeFit_Scribble_DetectedAndRejected()
    {
        List<SKPoint> pts = [];
        for (int i = 0; i < 8; i++)
        {
            pts.Add(new SKPoint(100, 100 + (i % 2 == 0 ? 0 : 25)));
            pts.Add(new SKPoint(150, 100 + (i % 2 == 0 ? 25 : 0)));
        }
        Assert.True(InkStrokeFit.IsScribble(pts));
        var fitted = InkStrokeFit.TryFitAnyShape(pts, new LineItem { StrokeWidth = 5 });
        Assert.Null(fitted);
    }

    [Fact]
    public void InkStrokeFit_Circle_FitsEllipseItem()
    {
        List<SKPoint> pts = [];
        for (int i = 0; i <= 24; i++)
        {
            float angle = (float)(i * 2.0 * Math.PI / 24.0);
            pts.Add(new SKPoint(100 + 50 * MathF.Cos(angle), 100 + 50 * MathF.Sin(angle)));
        }
        Assert.False(InkStrokeFit.IsScribble(pts));
        var fitted = InkStrokeFit.TryFitAnyShape(pts, new LineItem { StrokeWidth = 5 });
        var ellipse = Assert.IsType<EllipseItem>(fitted);
        Assert.InRange(ellipse.Bounds.Width, 95, 105);
        Assert.InRange(ellipse.Bounds.Height, 95, 105);
    }

    [Fact]
    public void InkStrokeFit_Rectangle_FitsRectItem()
    {
        List<SKPoint> pts =
        [
            new(50, 50), new(80, 50), new(120, 50), new(160, 50), new(200, 50),
            new(200, 80), new(200, 110), new(200, 150),
            new(160, 150), new(120, 150), new(80, 150), new(50, 150),
            new(50, 110), new(50, 80), new(50, 52),
        ];
        Assert.False(InkStrokeFit.IsScribble(pts));
        var fitted = InkStrokeFit.TryFitAnyShape(pts, new LineItem { StrokeWidth = 5 });
        var rect = Assert.IsType<RectItem>(fitted);
        Assert.InRange(rect.Bounds.Width, 140, 160);
        Assert.InRange(rect.Bounds.Height, 90, 110);
    }

    [Fact]
    public void PresenterRenderer_Undo_RemovesSnappedShapeDirectly()
    {
        var renderer = new PresenterRenderer { Tool = PresenterTool.Pen };
        var settings = new PresenterSettings();

        renderer.Begin(new SKPoint(0, 0), settings);
        renderer.Move(new SKPoint(50, 50), settings);
        renderer.Move(new SKPoint(100, 100), settings);

        var original = (FreehandItem)renderer.Draft!;
        var snappedShape = new LineItem { Start = new SKPoint(0, 0), End = new SKPoint(100, 100) };

        renderer.SnapDraft(snappedShape, original);
        renderer.End();

        Assert.True(renderer.HasInk);

        // Undo removes snapped shape directly (does not revert to rough drawing)
        bool undone = renderer.Undo();
        Assert.True(undone);
        Assert.False(renderer.HasInk);
    }

    [Fact]
    public void PresenterRenderer_Undo_RemovesReplacedItemDirectly()
    {
        var renderer = new PresenterRenderer { Tool = PresenterTool.Pen };
        var fh1 = new FreehandItem();
        var fh2 = new FreehandItem();
        var text = new TextItem { Text = "Test" };

        renderer.Replace([fh1, fh2], text);
        Assert.True(renderer.HasInk);

        bool undone = renderer.Undo();
        Assert.True(undone);
        Assert.False(renderer.HasInk);
    }

    [Fact]
    public void PresenterRenderer_SingleClick_CreatesDot()
    {
        var renderer = new PresenterRenderer { Tool = PresenterTool.Pen };
        var settings = new PresenterSettings();

        renderer.Begin(new SKPoint(150, 200), settings);
        renderer.End();

        Assert.True(renderer.HasInk);
        var dot = Assert.IsType<FreehandItem>(renderer.LastFreehand);
        Assert.Single(dot.Points);
        Assert.Equal(new SKPoint(150, 200), dot.Points[0]);

        Assert.False(dot.Bounds.IsEmpty);
        Assert.True(dot.Bounds.Contains(150, 200));

        Assert.True(renderer.Undo());
        Assert.False(renderer.HasInk);
    }

    [Fact]
    public void InkBeautifier_ResolveHandwritingFont_ReturnsValidFont()
    {
        string font = InkBeautifier.ResolveHandwritingFont();
        Assert.Equal("Ink Free", font);
    }

    [Fact]
    public void InkStrokeFit_Triangle_FitsPolygonItem()
    {
        List<SKPoint> pts =
        [
            new(100, 50), new(125, 100), new(150, 150), new(200, 250),
            new(150, 250), new(100, 250), new(0, 250),
            new(50, 150), new(75, 100), new(98, 52),
        ];
        Assert.False(InkStrokeFit.IsScribble(pts));
        var fitted = InkStrokeFit.TryFitAnyShape(pts, new LineItem { StrokeWidth = 5 });
        var poly = Assert.IsType<InkPolygonItem>(fitted);
        Assert.Equal(3, poly.Vertices.Length);
    }

    [Fact]
    public void InkStrokeFit_WavyUnderline_DoesNotBecomeStraightLine()
    {
        List<SKPoint> pts = [];
        for (int x = 0; x <= 200; x += 10)
        {
            float y = MathF.Sin(x * 0.1f) * 15f;
            pts.Add(new SKPoint(x, y));
        }
        var line = InkStrokeFit.TryFitLine(pts, new LineItem { StrokeWidth = 5 });
        Assert.Null(line);
    }

    [Fact]
    public void PresenterRenderer_MultipleStrokes_NeverDeletesPreviousStroke()
    {
        var renderer = new PresenterRenderer { Tool = PresenterTool.Pen };
        var settings = new PresenterSettings();

        renderer.Begin(new SKPoint(10, 10), settings);
        renderer.Move(new SKPoint(20, 20), settings);
        renderer.End();
        var stroke1 = renderer.LastFreehand;
        Assert.NotNull(stroke1);

        renderer.Begin(new SKPoint(100, 100), settings);
        renderer.Move(new SKPoint(200, 100), settings);
        renderer.End();

        var stroke2 = renderer.LastFreehand;
        Assert.NotNull(stroke2);

        var line = new LineItem { Start = new SKPoint(100, 100), End = new SKPoint(200, 100) };
        renderer.Replace([stroke2], line);

        // 1st undo: removes converted shape directly, leaving stroke1 intact
        Assert.True(renderer.Undo());
        Assert.Same(stroke1, renderer.LastFreehand);

        // 2nd undo: removes stroke1
        Assert.True(renderer.Undo());
        Assert.False(renderer.HasInk);
    }
}

public sealed class PresenterSettingsTests
{
    [Fact]
    public void Normalize_AppliesZoomItHotkeysOnce()
    {
        var settings = new AppSettings();
        settings.Normalize();
        Assert.True(settings.Presenter.Enabled);
        Assert.True(settings.Presenter.PenEnabled);
        Assert.Equal("D1", settings.Presenter.PenHotkey.Key);
        Assert.Equal(ModifierKeys.Control, settings.Presenter.PenHotkey.Modifiers);
        Assert.True(settings.Presenter.LaserEnabled);
        Assert.Equal("D2", settings.Presenter.LaserHotkey.Key);
        Assert.True(settings.Presenter.SpotlightEnabled);
        Assert.Equal("D3", settings.Presenter.SpotlightHotkey.Key);
        Assert.False(settings.Presenter.ArrowEnabled);
        Assert.Equal("Escape", settings.Presenter.CancelHotkey.Key);
        Assert.Equal(5, settings.Presenter.DefaultsRevision);
        Assert.True(settings.Presenter.CancelEnabled);
        Assert.Equal(5, settings.Presenter.InkColors.Count);
        Assert.Equal("#FFFFFFFF", settings.Presenter.InkColors[4]);
        Assert.Equal(settings.Presenter.PenColor, settings.Presenter.InkColors[0]);
    }

    [Fact]
    public void Normalize_ForcesCancelEnabled()
    {
        var settings = new AppSettings();
        settings.Normalize();
        settings.Presenter.CancelEnabled = false;
        settings.Normalize();
        Assert.True(settings.Presenter.CancelEnabled);
    }

    [Fact]
    public void Deserialize_PreservesPresenterPenWidth()
    {
        var settings = AppSettings.Deserialize("""
            {
              "Presenter": {
                "DefaultsRevision": 5,
                "PenWidth": 9
              }
            }
            """);

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(9, settings.Presenter.PenWidth);
    }

    [Fact]
    public void Normalize_ClampsPenWidth()
    {
        var p = new PresenterSettings { PenWidth = 99 };
        p.Normalize();
        Assert.Equal(24, p.PenWidth);
    }

    [Fact]
    public void PresenterSettings_InkShapeConvert_DefaultsToDrawAndHold()
    {
        var p = new PresenterSettings();
        p.Normalize();
        Assert.Equal(InkShapeConvertMode.DrawAndHold, p.InkShapeConvert);
    }
}
