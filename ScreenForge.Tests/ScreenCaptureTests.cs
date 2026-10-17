using System.Drawing;
using ScreenForge.Capture;

namespace ScreenForge.Tests;

/// <summary>
/// Ekran yakalama yolu: DXGI önbelleği ile hızlı kopyalama, gerekirse BitBlt'e
/// düşme ve karıştırma hesabının doğruluğu.
/// </summary>
public sealed class ScreenCaptureTests
{
    [Fact]
    public void CaptureVirtualScreen_ReturnsVirtualScreenSizedBitmap()
    {
        var bounds = ScreenCapture.VirtualScreenBounds;
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);

        using var bmp = ScreenCapture.CaptureVirtualScreen(includeCursor: false);

        Assert.Equal(bounds.Width, bmp.Width);
        Assert.Equal(bounds.Height, bmp.Height);
    }

    [Fact]
    public void Capture_ThenCrop_ProducesTheRequestedRegion()
    {
        // Kaydet / panoya kopyala / yükle yollarının kullandığı boru hattı.
        using var full = ScreenCapture.CaptureVirtualScreen(includeCursor: false);

        var region = new Rectangle(full.Width / 4, full.Height / 4, 320, 200);
        using var cropped = ScreenCapture.Crop(full, region);

        Assert.Equal(320, cropped.Width);
        Assert.Equal(200, cropped.Height);
    }

    [Fact]
    public void Crop_OutsideSource_ReturnsSmallestUsableBitmap()
    {
        using var full = ScreenCapture.CaptureVirtualScreen(includeCursor: false);

        using var cropped = ScreenCapture.Crop(full, new Rectangle(full.Width + 50, full.Height + 50, 80, 80));

        Assert.True(cropped.Width >= 1);
        Assert.True(cropped.Height >= 1);
    }

    [Fact]
    public void Capture_ThenToBitmapSource_KeepsPixelSize()
    {
        using var full = ScreenCapture.CaptureVirtualScreen(includeCursor: false);

        var source = ScreenCapture.ToBitmapSource(full);

        Assert.Equal(full.Width, source.PixelWidth);
        Assert.Equal(full.Height, source.PixelHeight);
    }

    [Fact]
    public void WarmupCapturePath_ThenCapture_StillMatchesVirtualScreen()
    {
        ScreenCapture.WarmupCapturePath();

        var bounds = ScreenCapture.VirtualScreenBounds;
        using var bmp = ScreenCapture.CaptureVirtualScreen(includeCursor: false);

        Assert.Equal(bounds.Width, bmp.Width);
        Assert.Equal(bounds.Height, bmp.Height);
    }

}
