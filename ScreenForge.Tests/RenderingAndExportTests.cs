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
}
