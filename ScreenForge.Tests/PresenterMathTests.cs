using SkiaSharp;
using ScreenForge.Editor;
using ScreenForge.Presenter;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class PresenterMathTests
{
    [Fact]
    public void OffsetsKeepingPoint_At1x_AreZero()
    {
        ZoomMath.OffsetsKeepingPoint(1f, 400, 300, out int x, out int y);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void OffsetsKeepingPoint_At2x_KeepsFocus()
    {
        ZoomMath.OffsetsKeepingPoint(2f, 400, 300, out int x, out int y);
        Assert.Equal(200, x);
        Assert.Equal(150, y);
    }

    [Fact]
    public void ClampOffsets_DoesNotExceedDesktop()
    {
        int ox = 5000, oy = 5000;
        ZoomMath.ClampOffsets(2f, 0, 0, 1000, 800, ref ox, ref oy);
        Assert.Equal(500, ox);
        Assert.Equal(400, oy);
    }

    [Fact]
    public void EaseOutCubic_Bounds()
    {
        Assert.Equal(0f, ZoomMath.EaseOutCubic(0));
        Assert.Equal(1f, ZoomMath.EaseOutCubic(1));
        Assert.True(ZoomMath.EaseOutCubic(0.5f) > 0.5f);
    }

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
        Assert.Equal("D2", settings.Presenter.PenHotkey.Key);
        Assert.Equal(ModifierKeys.Control, settings.Presenter.PenHotkey.Modifiers);
        Assert.True(settings.Presenter.LaserEnabled);
        Assert.Equal("D3", settings.Presenter.LaserHotkey.Key);
        Assert.True(settings.Presenter.SpotlightEnabled);
        Assert.Equal("D4", settings.Presenter.SpotlightHotkey.Key);
        Assert.False(settings.Presenter.ZoomPresets[1].Enabled);
        Assert.False(settings.Presenter.ZoomPresets[1].Hotkey.IsValid);
        Assert.False(settings.Presenter.ArrowEnabled);
        Assert.Equal("Escape", settings.Presenter.CancelHotkey.Key);
        Assert.Equal(1.5, settings.Presenter.ZoomPresets[0].Factor);
        Assert.Equal("D1", settings.Presenter.ZoomPresets[0].Hotkey.Key);
        Assert.Equal(2, settings.Presenter.ZoomPresets[1].Factor);
        Assert.Equal(4, settings.Presenter.DefaultsRevision);
        Assert.True(settings.Presenter.CancelEnabled);
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
    public void Deserialize_PreservesPresenterZoomPreset()
    {
        var settings = AppSettings.Deserialize("""
            {
              "Presenter": {
                "DefaultsRevision": 4,
                "PenWidth": 9,
                "ZoomPresets": [ { "Factor": 3.5 } ]
              }
            }
            """);

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(9, settings.Presenter.PenWidth);
        Assert.Equal(3.5, settings.Presenter.ZoomPresets[0].Factor);
    }

    [Fact]
    public void Normalize_ClampsAndFillsZoomPresets()
    {
        var p = new PresenterSettings
        {
            PenWidth = 99,
            ZoomPresets = [],
        };
        p.Normalize();
        Assert.Equal(24, p.PenWidth);
        Assert.Equal(2, p.ZoomPresets.Count);
    }
}
