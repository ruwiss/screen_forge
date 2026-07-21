using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class SceneClipboardTests
{
    [Fact]
    public void ClampTopLeft_KeepsRectInsideScene()
    {
        var tl = SceneClipboard.ClampTopLeft(90, 80, 30, 20, sceneW: 100, sceneH: 100);
        Assert.Equal(70, tl.X);
        Assert.Equal(80, tl.Y);
    }

    [Fact]
    public void ClampTopLeft_OversizedRectPinsToOrigin()
    {
        var tl = SceneClipboard.ClampTopLeft(-10, -5, 200, 150, sceneW: 100, sceneH: 80);
        Assert.Equal(0, tl.X);
        Assert.Equal(0, tl.Y);
    }

    [Fact]
    public void ClampTopLeft_NegativeOriginClampsToZero()
    {
        var tl = SceneClipboard.ClampTopLeft(-20, -10, 40, 30, sceneW: 100, sceneH: 100);
        Assert.Equal(0, tl.X);
        Assert.Equal(0, tl.Y);
    }

    [Fact]
    public void ComputeAnchorOffset_CentersOnAnchorThenClamps()
    {
        var union = new SKRect(0, 0, 40, 20);
        var (dx, dy) = SceneClipboard.ComputeAnchorOffset(union, anchorX: 50, anchorY: 50, sceneW: 100, sceneH: 100);
        // 50-20=30, 50-10=40 → ofset (30, 40)
        Assert.Equal(30, dx);
        Assert.Equal(40, dy);
    }

    [Fact]
    public void ComputeAnchorOffset_DoesNotLeaveScene()
    {
        var union = new SKRect(0, 0, 40, 20);
        var (dx, dy) = SceneClipboard.ComputeAnchorOffset(union, anchorX: 95, anchorY: 95, sceneW: 100, sceneH: 100);
        // left would be 75, top 85; after clamp left=60, top=80
        Assert.Equal(60, dx);
        Assert.Equal(80, dy);
    }

    [Fact]
    public void ComputeDuplicateOffset_UsesFixedOffsetAndClamps()
    {
        var union = new SKRect(10, 10, 30, 30);
        var (dx, dy) = SceneClipboard.ComputeDuplicateOffset(union, sceneW: 100, sceneH: 100, offset: 20);
        Assert.Equal(20, dx);
        Assert.Equal(20, dy);

        // Near edge: would go past → clamp
        var near = new SKRect(85, 85, 100, 100);
        var (dx2, dy2) = SceneClipboard.ComputeDuplicateOffset(near, sceneW: 100, sceneH: 100, offset: 20);
        Assert.Equal(0, dx2); // 85+20=105 → clamp left to 85 → dx 0? Wait: width=15, left max=85
        // left target 105 → clamp to 85 (100-15), so dx = 85-85 = 0
        Assert.Equal(0, dy2);
    }

    [Fact]
    public void OrderBySceneZ_PreservesSceneOrderNotSelectionOrder()
    {
        var scene = new Scene { CanvasSize = new SKSize(200, 200) };
        var a = new RectItem { Bounds = new SKRect(0, 0, 10, 10) };
        var b = new RectItem { Bounds = new SKRect(20, 0, 30, 10) };
        var c = new RectItem { Bounds = new SKRect(40, 0, 50, 10) };
        scene.Items.Add(a);
        scene.Items.Add(b);
        scene.Items.Add(c);

        // Seçim ters sırada
        var ordered = SceneClipboard.OrderBySceneZ(scene, new[] { c, a });
        Assert.Equal(new[] { a, c }, ordered);
    }

    [Fact]
    public void ImageItem_Clone_DeepCopiesBitmap()
    {
        using var src = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(SKColors.Red);
        var item = new ImageItem { Bitmap = src, Bounds = new SKRect(0, 0, 8, 8) };
        var clone = (ImageItem)item.Clone();

        Assert.NotSame(item.Bitmap, clone.Bitmap);
        Assert.Equal(item.Bitmap.Width, clone.Bitmap.Width);
        Assert.Equal(item.Bitmap.Height, clone.Bitmap.Height);

        // Kaynağı değiştir → kopya bozulmamalı
        src.Erase(SKColors.Blue);
        Assert.Equal(SKColors.Red, clone.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void RenderSelectionBitmap_ProducesNonEmptyPngSizedCanvas()
    {
        var a = new RectItem
        {
            Bounds = new SKRect(10, 20, 50, 60),
            StrokeColor = SKColors.White,
            FillColor = SKColors.Red,
            StrokeWidth = 1,
        };
        var b = new RectItem
        {
            Bounds = new SKRect(40, 50, 80, 90),
            StrokeColor = SKColors.White,
            FillColor = SKColors.Blue,
            StrokeWidth = 1,
        };

        using var bmp = SceneClipboard.RenderSelectionBitmap(new SceneItem[] { a, b });
        // union 10,20 → 80,90 = 70×70 + 2 pad
        Assert.Equal(72, bmp.Width);
        Assert.Equal(72, bmp.Height);
    }
}
