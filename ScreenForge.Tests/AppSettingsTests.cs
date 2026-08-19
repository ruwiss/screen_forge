using System.Globalization;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Deserialize_PreservesSavedToolStyleValues()
    {
        const string json = """
            {
              "ToolStyles": {
                "FontSize": 36,
                "StrokeWidth": 9,
                "StepSize": 48
              }
            }
            """;

        var settings = AppSettings.Deserialize(json);

        Assert.NotNull(settings);
        Assert.Equal(36, settings.ToolStyles.FontSize);
        Assert.Equal(9, settings.ToolStyles.StrokeWidth);
        Assert.Equal(48, settings.ToolStyles.StepSize);
    }

    [Fact]
    public void Defaults_OnlyRegionHotkeyIsAssigned()
    {
        var settings = new AppSettings();

        Assert.True(settings.RegionHotkey.IsValid);
        Assert.Equal("S", settings.RegionHotkey.Key);
        Assert.Equal(ModifierKeys.Alt | ModifierKeys.Shift, settings.RegionHotkey.Modifiers);

        Assert.False(settings.FullScreenHotkey.IsValid);
        Assert.False(settings.FullScreenUploadHotkey.IsValid);
        Assert.False(settings.CollageHotkey.IsValid);
        Assert.False(settings.QuickTranslateHotkey.IsValid);
    }

    [Fact]
    public void Normalize_TrUi_SetsNativeTrAndPairEn()
    {
        var settings = new AppSettings();

        settings.Normalize(new CultureInfo("tr-TR"));

        Assert.Equal("tr", settings.TranslateNativeLanguage);
        Assert.Equal("en", settings.TranslatePairLanguage);
        Assert.Equal("auto", settings.TranslateSourceLanguage);
    }

    [Fact]
    public void Normalize_EnUi_SetsNativeEnAndPairTr()
    {
        var settings = new AppSettings();

        settings.Normalize(new CultureInfo("en-US"));

        Assert.Equal("en", settings.TranslateNativeLanguage);
        Assert.Equal("tr", settings.TranslatePairLanguage);
    }

    [Fact]
    public void Normalize_UnknownUi_FallsBackToEn()
    {
        var settings = new AppSettings();

        settings.Normalize(new CultureInfo("sv-SE"));

        Assert.Equal("en", settings.TranslateNativeLanguage);
        Assert.Equal("tr", settings.TranslatePairLanguage);
    }

    [Fact]
    public void Normalize_DoesNotClobberUserLanguages()
    {
        var settings = AppSettings.Deserialize("""
            {
              "TranslateNativeLanguage": "de",
              "TranslatePairLanguage": "fr"
            }
            """);

        Assert.NotNull(settings);
        settings.Normalize(new CultureInfo("tr-TR"));

        Assert.Equal("de", settings.TranslateNativeLanguage);
        Assert.Equal("fr", settings.TranslatePairLanguage);
    }
}
