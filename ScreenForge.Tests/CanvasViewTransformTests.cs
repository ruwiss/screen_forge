using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class CanvasViewTransformTests
{
    [Fact]
    public void WpfToScene_At200Dpi_MapsScreenCenterToSceneCenter()
    {
        // 1000 DIP tuval, 2000 px Skia yüzeyi, sahne 1000 — orta nokta sahnenin ortası olmalı.
        var t = CanvasViewTransform.Compute(LayoutMode.OneToOne, sceneW: 1000, sceneH: 1000, pxW: 2000, pxH: 2000, dpi: 2);
        var p = CanvasViewTransform.WpfToScene(500, 500, actualW: 1000, actualH: 1000, 2000, 2000, t.Scale, t.Offset);

        Assert.InRange(p.X, 499, 501);
        Assert.InRange(p.Y, 499, 501);
    }

    [Fact]
    public void WpfToScene_WhenSurfaceIsDipSized_DoesNotHitSceneEdgeAtScreenCenter()
    {
        // IgnorePixelScaling benzeri: yüzey DIP, ölçek 1. Eski kod wpf*dpi/_scale ile 200% DPI'da
        // ekranın ortasında sahne kenarına çarpıyordu.
        var t = CanvasViewTransform.Compute(LayoutMode.OneToOne, 1000, 1000, pxW: 1000, pxH: 1000, dpi: 2);
        var p = CanvasViewTransform.WpfToScene(500, 500, 1000, 1000, 1000, 1000, t.Scale, t.Offset);

        Assert.InRange(p.X, 499, 501);
        Assert.InRange(p.Y, 499, 501);
        Assert.True(p.X < 900, "Ekran ortası sahne kenarına yapışmamalı");
    }

    [Fact]
    public void SceneToWpf_InvertsWpfToScene()
    {
        var t = CanvasViewTransform.Compute(LayoutMode.OneToOne, 800, 600, pxW: 1600, pxH: 1200, dpi: 2);
        var scene = CanvasViewTransform.WpfToScene(120, 80, 800, 600, 1600, 1200, t.Scale, t.Offset);
        var (x, y) = CanvasViewTransform.SceneToWpf(scene.X, scene.Y, 800, 600, 1600, 1200, t.Scale, t.Offset);

        Assert.InRange(x, 119.5, 120.5);
        Assert.InRange(y, 79.5, 80.5);
    }

    [Fact]
    public void FitLayout_CentersSmallScene()
    {
        var t = CanvasViewTransform.Compute(LayoutMode.Fit, sceneW: 100, sceneH: 50, pxW: 200, pxH: 200, dpi: 1);
        Assert.True(t.Offset.X > 0 || t.Offset.Y > 0);
        var origin = CanvasViewTransform.WpfToScene(t.Offset.X, t.Offset.Y, 200, 200, 200, 200, t.Scale, t.Offset);
        Assert.InRange(origin.X, -0.5, 0.5);
        Assert.InRange(origin.Y, -0.5, 0.5);
    }
}
