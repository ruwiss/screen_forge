using ScreenForge.Editor;
using ScreenForge.Gif.Editing;
using SkiaSharp;

namespace ScreenForge.Tests;

/// <summary>
/// Zamana bağlı sahnede yalnızca geçerli karedeki nesnelerin
/// seçilebildiğini doğrular.
/// </summary>
public sealed class SceneHitFilterTests
{
    [Fact]
    public void HitTest_FindsItemWithoutFilter()
    {
        var scene = new Scene { CanvasSize = new SKSize(100, 100) };
        var item = MakeRect(10, 10, 50, 50);
        scene.Items.Add(item);

        Assert.Same(item, scene.HitTest(new SKPoint(30, 30)));
    }

    [Fact]
    public void HitTest_SkipsFilteredItems()
    {
        var scene = new Scene { CanvasSize = new SKSize(100, 100) };
        var item = MakeRect(10, 10, 50, 50);
        scene.Items.Add(item);

        scene.HitFilter = _ => false;

        // O karede görünmeyen nesne tıklamayla seçilmemeli.
        Assert.Null(scene.HitTest(new SKPoint(30, 30)));
    }

    [Fact]
    public void HitTest_PicksTopmostAllowedItem()
    {
        var scene = new Scene { CanvasSize = new SKSize(100, 100) };

        var bottom = MakeRect(10, 10, 60, 60);
        var top = MakeRect(20, 20, 50, 50);
        scene.Items.Add(bottom);
        scene.Items.Add(top);

        // Üstteki süzülünce alttaki seçilebilir kalmalı.
        scene.HitFilter = i => !ReferenceEquals(i, top);

        Assert.Same(bottom, scene.HitTest(new SKPoint(30, 30)));
    }

    [Fact]
    public void ClipFilter_MatchesFrameCoverage()
    {
        var track = new AnnotationTrack(new SKSize(100, 100));

        var early = MakeRect(10, 10, 50, 50);
        track.Scene.Items.Add(early);
        track.Register(early, 0, 3);

        track.Scene.HitFilter = item => track.ClipOf(item).CoversFrame(_currentFrame);

        // Nesnenin kapsadığı karede seçilebilir.
        _currentFrame = 2;
        Assert.Same(early, track.Scene.HitTest(new SKPoint(30, 30)));

        // Kapsamadığı karede seçilemez — bildirilen hata buydu.
        _currentFrame = 9;
        Assert.Null(track.Scene.HitTest(new SKPoint(30, 30)));
    }

    private int _currentFrame;

    private static RectItem MakeRect(float left, float top, float right, float bottom) => new()
    {
        Bounds = new SKRect(left, top, right, bottom),
        FillColor = new SKColor(0xFF, 0, 0),
        StrokeColor = new SKColor(0xFF, 0, 0),
        StrokeWidth = 2,
    };
}
