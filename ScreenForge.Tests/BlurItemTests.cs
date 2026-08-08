using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class BlurItemTests
{
    [Fact]
    public void Clone_DoesNotShareBaked()
    {
        var blur = new BlurItem { Bounds = new SKRect(0, 0, 40, 40), Strength = 4f };
        using var bmp = new SKBitmap(20, 20, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Red);
        blur.SetBaked(bmp.Copy());

        var clone = (BlurItem)blur.Clone();
        Assert.Null(clone.BakedBitmap);
        Assert.True(clone.NeedsBake);
        Assert.NotNull(blur.BakedBitmap);
    }

    [Fact]
    public void StrengthChange_MarksNeedsBake()
    {
        var blur = new BlurItem { Strength = 4f, NeedsBake = false };
        blur.Strength = 7f;
        Assert.True(blur.NeedsBake);
    }

    [Fact]
    public void Bake_ThenRender_DoesNotThrow()
    {
        using var bg = new SKBitmap(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.CornflowerBlue);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(20, 20, 100, 80), Strength = 6f };
        scene.Items.Add(blur);

        SceneRenderer.BakeDirtyBlurs(scene);
        Assert.False(blur.NeedsBake);
        Assert.NotNull(blur.BakedBitmap);

        using var t = new SKBitmap(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(t);
        SceneRenderer.RenderContent(c, scene);
    }

    [Fact]
    public void DragAndResize_UsesBakedOnly_NoRebake()
    {
        using var bg = new SKBitmap(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.SteelBlue);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(100, 100, 250, 200), Strength = 8f };
        scene.Items.Add(blur);

        SceneRenderer.BakeDirtyBlurs(scene);
        var baked = blur.BakedBitmap;
        Assert.NotNull(baked);

        using var t = new SKBitmap(400, 300, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(t);

        // Taşıma: bake aynı kalmalı
        for (int i = 0; i < 50; i++)
        {
            blur.Move(2, 1);
            SceneRenderer.RenderContent(c, scene);
        }
        Assert.Same(baked, blur.BakedBitmap);
        Assert.False(blur.NeedsBake);

        // Resize: bake aynı (commit'te NeedsBake olur)
        for (int i = 0; i < 50; i++)
        {
            float d = i;
            blur.Bounds = new SKRect(100 - d, 100 - d, 250 + d, 200 + d);
            SceneRenderer.RenderContent(c, scene);
        }
        Assert.Same(baked, blur.BakedBitmap);
    }

    [Fact]
    public void DragPreview_RendersWithoutUsingBaked()
    {
        using var bg = new SKBitmap(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.Orange);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(20, 20, 100, 80), Strength = 8f };
        scene.Items.Add(blur);
        SceneRenderer.BakeDirtyBlurs(scene);
        Assert.NotNull(blur.BakedBitmap);

        blur.DragPreview = true;
        blur.Bounds = new SKRect(40, 40, 160, 120);

        using var t = new SKBitmap(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(t);
        SceneRenderer.RenderContent(c, scene); // ghost; baked değişmez
        Assert.NotNull(blur.BakedBitmap);

        blur.DragPreview = false;
        blur.NeedsBake = true;
        SceneRenderer.BakeDirtyBlurs(scene);
        Assert.False(blur.NeedsBake);
    }

    [Fact]
    public void LargeBackground_120Frames_DragResize_Fast()
    {
        using var bg = new SKBitmap(1920, 1080, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.CadetBlue);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(200, 150, 500, 400), Strength = 8f };
        scene.Items.Add(blur);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        SceneRenderer.BakeDirtyBlurs(scene);

        using var target = new SKBitmap(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(target);
        for (int i = 0; i < 60; i++)
        {
            blur.Move(2, 1);
            SceneRenderer.RenderContent(canvas, scene);
        }
        for (int i = 0; i < 60; i++)
        {
            float d = i;
            blur.Bounds = new SKRect(200 - d, 150 - d, 500 + d, 400 + d);
            SceneRenderer.RenderContent(canvas, scene);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8_000, $"Too slow: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void CommitSimulate_RebakeAfterResize()
    {
        using var bg = new SKBitmap(400, 300, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.Olive);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(40, 40, 120, 100), Strength = 5f };
        scene.Items.Add(blur);
        SceneRenderer.BakeDirtyBlurs(scene);
        var first = blur.BakedBitmap;
        Assert.NotNull(first);

        var before = blur.Clone();
        blur.Bounds = new SKRect(10, 10, 200, 180);
        blur.NeedsBake = true;
        var after = blur.Clone();
        scene.Apply(new ModifyItemAction(blur, before, after));
        // Apply → test harness doesn't wire OnSceneChanged; bake explicit
        SceneRenderer.BakeDirtyBlurs(scene);

        Assert.False(blur.NeedsBake);
        Assert.NotNull(blur.BakedBitmap);
        Assert.NotSame(first, blur.BakedBitmap);
    }

    [Fact]
    public void RenderToBitmap_BakesAutomatically()
    {
        using var bg = new SKBitmap(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.Navy);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(10, 10, 50, 50), Strength = 4f };
        scene.Items.Add(blur);
        Assert.True(blur.NeedsBake);

        using var bmp = SceneRenderer.RenderToBitmap(scene);
        Assert.Equal(100, bmp.Width);
        Assert.False(blur.NeedsBake);
        Assert.NotNull(blur.BakedBitmap);
    }

    [Fact]
    public void Pixelate_Bake_Works()
    {
        using var bg = new SKBitmap(120, 120, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.Lime);
        var scene = new Scene { Background = bg };
        var blur = new BlurItem { Bounds = new SKRect(20, 20, 80, 80), Strength = 8f, Pixelate = true };
        scene.Items.Add(blur);
        SceneRenderer.BakeDirtyBlurs(scene);
        Assert.NotNull(blur.BakedBitmap);

        using var t = new SKBitmap(120, 120, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(t);
        blur.Bounds = new SKRect(0, 0, 120, 120);
        blur.Render(c);
    }

    [Fact]
    public void TwoBlurs_BakeIndependently()
    {
        using var bg = new SKBitmap(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
        bg.Erase(SKColors.DarkSlateGray);
        var scene = new Scene { Background = bg };
        var a = new BlurItem { Bounds = new SKRect(10, 10, 60, 60), Strength = 5f };
        var b = new BlurItem { Bounds = new SKRect(80, 80, 150, 150), Strength = 5f };
        scene.Items.Add(a);
        scene.Items.Add(b);
        SceneRenderer.BakeDirtyBlurs(scene);
        Assert.NotNull(a.BakedBitmap);
        Assert.NotNull(b.BakedBitmap);
        Assert.NotSame(a.BakedBitmap, b.BakedBitmap);
    }
}
