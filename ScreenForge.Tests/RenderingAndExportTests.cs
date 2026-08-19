using ScreenForge.Editor;
using ScreenForge.Settings;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class RenderingAndExportTests
{
    [Fact]
    public void RenderToBitmap_AppliesItemOpacityOnce()
    {
        var scene = new Scene { CanvasSize = new SKSize(20, 20) };
        scene.Items.Add(new RectItem
        {
            Bounds = new SKRect(0, 0, 20, 20),
            FillColor = SKColors.Red,
            StrokeColor = SKColors.Red,
            StrokeWidth = 0,
            Opacity = 0.5f,
        });

        using var bitmap = SceneRenderer.RenderToBitmap(scene);

        Assert.InRange(bitmap.GetPixel(10, 10).Alpha, 124, 130);
    }

    [Fact]
    public void SaveEncoded_TruncatesExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, new byte[16_384]);
            using var bitmap = new SKBitmap(2, 2);
            bitmap.Erase(SKColors.OrangeRed);
            using var data = ImageExporter.Encode(bitmap, ImageFormat.Png, 100);

            ImageExporter.SaveEncoded(data, path);

            Assert.Equal(data.Size, new FileInfo(path).Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RenderToBitmap_TransparentBackground_KeepsClearPixels()
    {
        var scene = new Scene
        {
            CanvasSize = new SKSize(8, 8),
            BackgroundColor = SKColors.Transparent,
        };

        using var bitmap = SceneRenderer.RenderToBitmap(scene);

        Assert.Equal(0, bitmap.GetPixel(4, 4).Alpha);
    }

    [Fact]
    public void CropBitmap_KeepsAlphaOutsideOpaqueRect()
    {
        using var src = new SKBitmap(20, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(src))
        using (var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill })
        {
            c.Clear(SKColors.Transparent);
            c.DrawRect(new SKRect(5, 2, 15, 8), paint);
        }

        using var cropped = SceneRenderer.CropBitmap(src, new SKRect(4, 1, 16, 9));

        Assert.Equal(12, cropped.Width);
        Assert.Equal(8, cropped.Height);
        Assert.Equal(0, cropped.GetPixel(0, 0).Alpha);
        Assert.Equal(255, cropped.GetPixel(6, 4).Alpha);
    }

    [Fact]
    public void EncodePng_PreservesFullyTransparentPixel()
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(1, 1, new SKColor(255, 0, 0, 255));

        using var data = ImageExporter.Encode(bitmap, ImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data.ToArray());

        Assert.NotNull(decoded);
        Assert.Equal(0, decoded!.GetPixel(0, 0).Alpha);
        Assert.Equal(255, decoded.GetPixel(1, 1).Red);
    }

    [Fact]
    public void UnpremultiplyBgraRow_ExpandsPremulRgb()
    {
        // Premul kırmızı %50: B=0 G=0 R=128 A=128 → düz R≈255
        var src = new byte[] { 0, 0, 128, 128 };
        var dst = new byte[4];

        ImageExporter.UnpremultiplyBgraRow(src, dst);

        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.InRange(dst[2], 250, 255);
        Assert.Equal(128, dst[3]);
    }

    [Fact]
    public void CopyUnpremultipliedBgraBottomUp_FlipsAndKeepsClearPixels()
    {
        using var bmp = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        bmp.SetPixel(0, 0, new SKColor(0, 255, 0, 255)); // üst-sol yeşil
        var dest = new byte[2 * 2 * 4];

        ImageExporter.CopyUnpremultipliedBgraBottomUp(bmp, dest);

        // Bottom-up: ilk satır kaynak y=1 (şeffaf)
        Assert.Equal(0, dest[3]);
        // İkinci satır kaynak y=0, x=0 yeşil
        Assert.Equal(255, dest[8 + 1]); // G
        Assert.Equal(255, dest[8 + 3]); // A
    }
}
