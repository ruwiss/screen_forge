using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class SceneTests
{
    [Fact]
    public void SceneCropAction_SupportsUndoAndRedo()
    {
        var scene = new Scene { CanvasSize = new SKSize(100, 80) };
        var item = new RectItem { Bounds = new SKRect(20, 10, 40, 30) };
        scene.Items.Add(item);

        scene.Apply(new SceneCropAction(scene, new SKRect(10, 5, 90, 75)));

        Assert.Equal(new SKSize(80, 70), scene.CanvasSize);
        Assert.Equal(new SKRect(10, 5, 30, 25), item.Bounds);

        scene.Undo();

        Assert.Equal(new SKSize(100, 80), scene.CanvasSize);
        Assert.Equal(new SKRect(20, 10, 40, 30), item.Bounds);

        scene.Redo();

        Assert.Equal(new SKSize(80, 70), scene.CanvasSize);
        Assert.Equal(new SKRect(10, 5, 30, 25), item.Bounds);
    }
}
