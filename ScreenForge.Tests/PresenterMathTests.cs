using SkiaSharp;
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
