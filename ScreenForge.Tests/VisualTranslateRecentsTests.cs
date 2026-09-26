using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class VisualTranslateRecentsTests
{
    [Fact]
    public void Remember_SkipsDefault_AndPutsLatestFirst()
    {
        var settings = new AppSettings
        {
            TranslateNativeLanguage = "tr",
            TranslatePairLanguage = "en",
        };

        VisualTranslateRecents.Remember(settings, "en");
        VisualTranslateRecents.Remember(settings, "de");
        VisualTranslateRecents.Remember(settings, "fr");

        Assert.Equal(["fr", "de"], VisualTranslateRecents.Visible(settings));
    }

    [Fact]
    public void Visible_HidesRecentThatMatchesCurrentDefault()
    {
        var settings = new AppSettings
        {
            TranslateNativeLanguage = "tr",
            TranslatePairLanguage = "de",
            RecentVisualLanguages = ["de", "fr"],
        };

        Assert.Equal(["fr"], VisualTranslateRecents.Visible(settings));
    }

    [Fact]
    public void Remember_CapsAtFive()
    {
        var settings = new AppSettings
        {
            TranslateNativeLanguage = "tr",
            TranslatePairLanguage = "en",
        };

        foreach (var code in new[] { "de", "fr", "es", "it", "pt", "ru" })
            VisualTranslateRecents.Remember(settings, code);

        Assert.Equal(5, settings.RecentVisualLanguages.Count);
        Assert.Equal("ru", settings.RecentVisualLanguages[0]);
        Assert.DoesNotContain("de", settings.RecentVisualLanguages);
    }
}
