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
}
