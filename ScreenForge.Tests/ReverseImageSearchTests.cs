using ScreenForge.Search;

namespace ScreenForge.Tests;

public sealed class ReverseImageSearchTests
{
    [Fact]
    public void TryParseYandexCbir_Fixture_ReadsIdAndUrl()
    {
        const string json = """{"blocks":[{"params":{"cbirId":"abc123","originalImageUrl":"https://i.yandex/x"}}]}""";
        Assert.True(ReverseImageSearch.TryParseYandexCbir(json, out string cbirId, out string? originalImageUrl));
        Assert.Equal("abc123", cbirId);
        Assert.Equal("https://i.yandex/x", originalImageUrl);
    }

    [Fact]
    public void TryParseYandexCbir_MissingCbirId_ReturnsFalse()
    {
        const string json = """{"blocks":[{"params":{"originalImageUrl":"https://i.yandex/x"}}]}""";
        Assert.False(ReverseImageSearch.TryParseYandexCbir(json, out string cbirId, out string? originalImageUrl));
        Assert.Equal("", cbirId);
        Assert.Null(originalImageUrl);
    }

    [Fact]
    public void TryParseYandexCbir_Garbage_ReturnsFalse()
    {
        Assert.False(ReverseImageSearch.TryParseYandexCbir("not-json", out _, out _));
    }

    [Fact]
    public void BuildLensUploadHtml_PostsEncodedImage()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        string html = ReverseImageSearch.BuildLensUploadHtml(png);
        Assert.Contains("https://lens.google.com/v3/upload", html);
        Assert.Contains("name=\"encoded_image\"", html);
        Assert.Contains(Convert.ToBase64String(png), html);
        Assert.Contains("document.getElementById(\"f\").submit()", html);
    }
}
