using ScreenForge.Translate;

namespace ScreenForge.Tests;

public sealed class TranslateLanguageRouterTests
{
    [Fact]
    public void TrNative_TrSource_RoutesToPair()
    {
        Assert.True(TranslateLanguageRouter.ShouldTranslateToPair(
            "tr", "tr", "Merhaba", "Merhaba"));
    }

    [Fact]
    public void TrNative_EnSource_KeepsNative()
    {
        Assert.False(TranslateLanguageRouter.ShouldTranslateToPair(
            "tr", "en", "Hello", "Merhaba"));
    }

    [Fact]
    public void IdentityWithoutSource_RoutesToPair()
    {
        Assert.True(TranslateLanguageRouter.ShouldTranslateToPair(
            "tr", "", "Merhaba", "Merhaba"));
    }

    [Fact]
    public void IdentityEnglish_WithEnSource_DoesNotRouteToPair()
    {
        Assert.False(TranslateLanguageRouter.ShouldTranslateToPair(
            "tr", "en", "Hello", "Hello"));
    }

    [Fact]
    public void ZhCnMatchesZh()
    {
        Assert.True(TranslateLanguageRouter.LanguagesEqual("zh-CN", "zh"));
    }

    [Fact]
    public void ImageRoute_AlwaysAutoSourceToNative()
    {
        var (target, source) = TranslateLanguageRouter.ImageRoute("tr");
        Assert.Equal("tr", target);
        Assert.Null(source);
    }
}
