using ScreenForge.Translate;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class GoogleLensClientTests
{
    [Fact]
    public void BuildOcrRequest_DiffersFromTranslateRequest()
    {
        byte[] png = TinyPng();
        byte[] ocr = GoogleLensClient.BuildOcrRequest(png, 8, 8);
        byte[] translate = GoogleLensClient.BuildRequest(png, 8, 8, "tr", null);
        Assert.NotEqual(translate, ocr);
    }

    [Fact]
    public void BuildOcrRequest_IsProtobufPayload()
    {
        byte[] png = TinyPng();
        byte[] ocr = GoogleLensClient.BuildOcrRequest(png, 8, 8);
        Assert.True(ocr.Length > 32);
    }

    private static byte[] TinyPng()
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return data.ToArray();
    }
}
