using System.Drawing;
using System.Runtime.InteropServices;
using ScreenForge.Capture;
using CaptureTesting;
using ScreenForge.Settings;
using ScreenForge.Windows;

namespace ScreenForge.Tests;

[CollectionDefinition("Masaüstü yakalama", DisableParallelization = true)]
public sealed class DesktopCaptureCollection;

/// <summary>Değişiklikten hemen sonraki canlı pikseli yirmi kez doğrular; önbellekli kareyi kabul etmez.</summary>
[Collection("Masaüstü yakalama")]
public sealed class CaptureFreshnessTests
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    [Fact]
    public void PrepareForInstantShow_DoesNotChangeVisibleDesktopOrForeground()
    {
        WpfRunner.Run(() =>
        {
            using var target = new DesktopStimulus();
            target.Present(Color.FromArgb(224, 96, 64));
            var win = new CaptureOverlayWindow(new Bitmap(480, 340), new Rectangle(target.Left, target.Top, 480, 340), new AppSettings());
            try
            {
                nint foreground = GetForegroundWindow();
                win.PrepareForInstantShow();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                Assert.Equal(0, DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)));
                Assert.NotEqual(0, cloaked);
                int samples = 0;
                WpfRunner.DrainUntil(() =>
                {
                    Assert.Equal(Color.FromArgb(224, 96, 64).ToArgb(), target.ReadPresentedPixel().ToArgb());
                    samples++;
                    return !win.IsVisible;
                });
                Assert.False(win.IsVisible);
                Assert.Equal(foreground, GetForegroundWindow());
                Assert.True(samples > 0);
            }
            finally { win.Close(); }
        });
    }

    [Fact]
    public void CaptureVirtualScreen_AfterColorChanges_ReturnsCurrentPixelsTwentyTimes()
    {
        WpfRunner.Run(() =>
        {
            using var target = new DesktopStimulus();
            // İlk kareyi de iste: sürekli kaynak kullanan eski yolun önceki kareyi
            // tutması için aynı oturumu kullanıyoruz, yakalamalar arasında beklemiyoruz.
            using (var initial = ScreenCapture.CaptureVirtualScreen(false)) { }
            var bounds = ScreenCapture.VirtualScreenBounds;
            Color[] colors = [Color.FromArgb(224, 96, 64), Color.FromArgb(64, 208, 144)];
            int passed = 0;
            var failures = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                target.Present(colors[i % 2]); // Uyarıcı gerçekten sunulsun; yakalama öncesi Sleep yok.
                using var capture = ScreenCapture.CaptureVirtualScreen(false);
                var pixel = capture.GetPixel(target.Left + 90 - bounds.X, target.Top + 60 - bounds.Y);
                if (pixel.ToArgb() == colors[i % 2].ToArgb()) passed++;
                else failures.Add($"{i + 1}: beklenen={colors[i % 2].ToArgb():X8}, yakalanan={pixel.ToArgb():X8}, ekrandaki={target.ReadPresentedPixel().ToArgb():X8}, bounds={bounds}");
            }
            Assert.True(passed == 20, $"{passed}/20 taze; " + string.Join("; ", failures));
        });
    }
}
