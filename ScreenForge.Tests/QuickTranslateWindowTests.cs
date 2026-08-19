using System.Windows;
using System.Windows.Controls;
using ScreenForge.Settings;
using ScreenForge.Windows;

namespace ScreenForge.Tests;

[Collection("WPF")]
public sealed class QuickTranslateWindowTests
{
    [Fact]
    public void Constructor_WithSelectedText_SeedsInputBox()
    {
        WpfRunner.Run(() =>
        {
            // Seçili metin kurucuda yazılıyor; zamanlayıcılar ondan önce hazır olmalı.
            var window = new QuickTranslateWindow(new AppSettings(), "Merhaba dünya");

            var input = (TextBox)window.FindName("TxtInput")!;
            Assert.Equal("Merhaba dünya", input.Text);

            window.Close();
        });
    }

    [Fact]
    public void UseIncomingText_ReplacesExistingInput()
    {
        WpfRunner.Run(() =>
        {
            var window = new QuickTranslateWindow(new AppSettings(), "ilk");
            window.UseIncomingText("ikinci metin");

            var input = (TextBox)window.FindName("TxtInput")!;
            Assert.Equal("ikinci metin", input.Text);

            window.Close();
        });
    }

    [Fact]
    public void CopyButton_UsesIconInsteadOfLabel()
    {
        WpfRunner.Run(() =>
        {
            var window = new QuickTranslateWindow(new AppSettings());
            var button = (Button)window.FindName("BtnCopy")!;

            Assert.IsType<System.Windows.Shapes.Path>(button.Content);
            Assert.Equal("Çeviriyi kopyala", button.ToolTip);

            window.Close();
        });
    }
}
