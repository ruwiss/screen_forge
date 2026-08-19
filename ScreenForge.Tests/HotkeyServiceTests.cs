using ScreenForge.Hotkeys;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

[Collection("WPF")]
public sealed class HotkeyServiceTests
{
    [Fact]
    public void Register_EmptyQuickTranslateHotkey_IsSkipped()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            using var service = new HotkeyService();

            bool registered = service.Register(
                settings.QuickTranslateHotkey,
                () => { },
                "Hızlı çeviri");

            Assert.False(settings.QuickTranslateHotkey.IsValid);
            Assert.False(registered);
            Assert.Empty(service.FailedRegistrations);
        });
    }
}
