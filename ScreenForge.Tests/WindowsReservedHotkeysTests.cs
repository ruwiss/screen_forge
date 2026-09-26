using ScreenForge.Hotkeys;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class WindowsReservedHotkeysTests
{
    [Fact]
    public void Describe_KnownWindowsChords()
    {
        Assert.Contains("Ekran alıntısı", WindowsReservedHotkeys.Describe(new HotkeyConfig
        {
            Modifiers = ModifierKeys.Windows | ModifierKeys.Shift,
            Key = "S",
        })!);

        Assert.Contains("Görev değiştirme", WindowsReservedHotkeys.Describe(new HotkeyConfig
        {
            Modifiers = ModifierKeys.Alt,
            Key = "Tab",
        })!);
    }

    [Fact]
    public void Describe_AppDefault_IsNotReserved()
    {
        Assert.Null(WindowsReservedHotkeys.Describe(AppSettings.DefaultRegionHotkey()));
        Assert.Null(WindowsReservedHotkeys.Describe(AppSettings.DefaultQuickTranslateHotkey()));
        Assert.Null(WindowsReservedHotkeys.Describe(AppSettings.UnassignedHotkey()));
    }

    [Fact]
    public void CopyFrom_RestoresFactoryShortcut()
    {
        var config = new HotkeyConfig { Key = "", Modifiers = ModifierKeys.None };
        config.CopyFrom(AppSettings.DefaultRegionHotkey());

        Assert.Equal("S", config.Key);
        Assert.Equal(ModifierKeys.Alt | ModifierKeys.Shift, config.Modifiers);
    }
}
