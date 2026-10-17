using System.Drawing;
using System.Windows.Media.Imaging;
using ScreenForge.Settings;
using ScreenForge.Windows;

namespace ScreenForge.Tests;

/// <summary>
/// Hazır overlay penceresinin kısayol anındaki güncellenmesi: görüntü değişimi,
/// ekran sınırlarının güncellenmesi ve ön gösterim sonrası pencerenin gizli kalması.
/// </summary>
public sealed class CaptureOverlayScreenshotTests
{
    [Fact]
    public void Close_DuringPreparation_CancelsPendingRenderAndTimeout()
    {
        WpfRunner.Run(() =>
        {
            Exception? failure = null;
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Windows.Threading.DispatcherUnhandledExceptionEventHandler handler = (_, e) => { failure = e.Exception; e.Handled = true; };
            dispatcher.UnhandledException += handler;
            try
            {
                var win = new CaptureOverlayWindow(new Bitmap(64, 48), new Rectangle(0, 0, 64, 48), new AppSettings());
                win.PrepareForInstantShow();
                win.Close();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                WpfRunner.DrainUntil(() => clock.ElapsedMilliseconds >= 750);
                Assert.Null(failure);
                Assert.False(win.IsVisible);
            }
            finally { dispatcher.UnhandledException -= handler; }
        });
    }

    [Fact]
    public void SetScreenshot_DuringPreparation_CannotBeHiddenByOldCallback()
    {
        WpfRunner.Run(() =>
        {
            var bounds = new Rectangle(0, 0, 64, 48);
            var win = new CaptureOverlayWindow(new Bitmap(64, 48), bounds, new AppSettings());
            try
            {
                win.PrepareForInstantShow();
                var current = new Bitmap(64, 48);
                using (var g = Graphics.FromImage(current)) g.Clear(Color.Lime);
                win.SetScreenshot(current, bounds, CaptureMode.Region);
                win.Show();
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                WpfRunner.DrainUntil(() => elapsed.ElapsedMilliseconds >= 750);
                Assert.True(win.IsVisible);
                Assert.Equal(Color.Lime.ToArgb(), win.CurrentScreenshot.GetPixel(20, 20).ToArgb());
            }
            finally { win.Close(); }
        });
    }

    [Fact]
    public void SetScreenshot_AfterPreparation_ReplacesPresentedPixelsAndExportPixels()
    {
        WpfRunner.Run(() =>
        {
            var bounds = new Rectangle(0, 0, 64, 48);
            var old = new Bitmap(64, 48);
            using (var g = Graphics.FromImage(old)) g.Clear(Color.Red);
            var win = new CaptureOverlayWindow(old, bounds, new AppSettings());
            try
            {
                win.PrepareForInstantShow();
                WpfRunner.DrainUntil(() => !win.IsVisible);
                var current = new Bitmap(64, 48);
                using (var g = Graphics.FromImage(current)) g.Clear(Color.Blue);
                win.SetScreenshot(current, bounds, CaptureMode.Region);
                var source = Assert.IsAssignableFrom<BitmapSource>(win.ScreenImage.Source);
                byte[] pixel = new byte[4];
                source.CopyPixels(new System.Windows.Int32Rect(20, 20, 1, 1), pixel, 4, 0);
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixel);
                using var crop = ScreenForge.Capture.ScreenCapture.Crop(win.CurrentScreenshot, new Rectangle(10, 10, 20, 20));
                Assert.Equal(Color.Blue.ToArgb(), crop.GetPixel(5, 5).ToArgb());
            }
            finally { win.Close(); }
        });
    }

    [Fact]
    public void SetScreenshot_ReplacesImageAndBounds()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            using var first = new Bitmap(64, 48, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var second = new Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var win = new CaptureOverlayWindow(first, new Rectangle(0, 0, 64, 48), settings, CaptureMode.Region);
            win.SetScreenshot(second, new Rectangle(10, 20, 32, 24), CaptureMode.Region);

            Assert.Equal(new Rectangle(10, 20, 32, 24), win.VirtualBounds);

            var src = Assert.IsAssignableFrom<BitmapSource>(win.ScreenImage.Source);
            Assert.Equal(32, src.PixelWidth);
            Assert.Equal(24, src.PixelHeight);
        });
    }

    [Fact]
    public void SetScreenshot_KeepsTheNewBitmapAsScreenshot()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            using var first = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var second = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var win = new CaptureOverlayWindow(first, new Rectangle(0, 0, 8, 8), settings, CaptureMode.Region);
            win.SetScreenshot(second, new Rectangle(0, 0, 16, 16), CaptureMode.Region);

            Assert.Equal(16, win.CurrentScreenshot.Width);
            Assert.Equal(16, win.CurrentScreenshot.Height);
        });
    }

    [Fact]
    public void SetScreenshot_DisposesThePreviousScreenshot()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            var first = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var second = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var win = new CaptureOverlayWindow(first, new Rectangle(0, 0, 8, 8), settings, CaptureMode.Region);
            win.SetScreenshot(second, new Rectangle(0, 0, 16, 16), CaptureMode.Region);

            // Eski görüntü bırakılmış olmalı; aksi hâlde her kısayol tam ekran
            // görüntüsü kadar bellek sızdırırdı.
            Assert.ThrowsAny<Exception>(() => first.GetPixel(0, 0));
            Assert.Same(second, win.CurrentScreenshot);
        });
    }

    [Fact]
    public void Close_DisposesTheScreenshot()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            var bmp = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var win = new CaptureOverlayWindow(bmp, new Rectangle(0, 0, 8, 8), settings, CaptureMode.Region);
            win.Close();

            // Pencere kapandığında ekran görüntüsü de bırakılmalı; aksi hâlde atılan
            // hazırlık pencereleri tam ekran görüntüsü kadar bellekte kalırdı.
            Assert.ThrowsAny<Exception>(() => bmp.GetPixel(0, 0));
        });
    }

    [Fact]
    public void PrepareForInstantShow_EndsWithHiddenWindowAtItsBounds()
    {
        WpfRunner.Run(() =>
        {
            var settings = new AppSettings();
            using var placeholder = new Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bounds = new Rectangle(0, 0, 32, 24);

            var win = new CaptureOverlayWindow(placeholder, bounds, settings, CaptureMode.Region);
            win.PrepareForInstantShow();

            // Ön gösterim bittiğinde pencere gizli ve kendi sınırlarında olmalı.
            WpfRunner.DrainUntil(() => !win.IsVisible, 15000);

            Assert.False(win.IsVisible);
            Assert.Equal(bounds, win.VirtualBounds);
        });
    }
}
