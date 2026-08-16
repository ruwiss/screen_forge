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
    }
}
