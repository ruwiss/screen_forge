using ScreenForge.Windows;
using WpfRect = System.Windows.Rect;

namespace ScreenForge.Tests;

public sealed class TranslateResultLayoutTests
{
    // İki 1920×1080 monitör, sanal masaüstü 3840×1080.
    private static readonly WpfRect LeftMon = new(0, 0, 1920, 1080);
    private static readonly WpfRect RightMon = new(1920, 0, 1920, 1080);

    [Fact]
    public void Host_OnLeftMonitor_StaysInsideThatMonitor()
    {
        var host = TranslateResultLayout.Host(LeftMon, imgW: 800, imgH: 600, selW: 400, selH: 300);

        Assert.InRange(host.Left, LeftMon.Left, LeftMon.Right - host.Width);
        Assert.InRange(host.Top, LeftMon.Top, LeftMon.Bottom - host.Height);
        Assert.True(host.Left + host.Width / 2 < 1920, "Sol monitörde merkez sanal masaüstü ortasına kaymamalı");
        Assert.InRange(host.Left + host.Width / 2, 950, 970);
        Assert.InRange(host.Top + host.Height / 2, 530, 550);
    }

    [Fact]
    public void Host_OnRightMonitor_DoesNotStraddleTheSeam()
    {
        var host = TranslateResultLayout.Host(RightMon, imgW: 800, imgH: 600, selW: 400, selH: 300);

        Assert.True(host.Left >= RightMon.Left);
        Assert.True(host.Left + host.Width <= RightMon.Right);
        Assert.InRange(host.Left + host.Width / 2, 1920 + 950, 1920 + 970);
    }

    [Fact]
    public void Host_DoesNotUseVirtualDesktopCenter()
    {
        // Eski kod ActualWidth=3840 ile (3840-hostW)/2 ≈ 1920 civarı, dikişte yarım kalır.
        var host = TranslateResultLayout.Host(LeftMon, imgW: 1200, imgH: 800, selW: 600, selH: 400);
        Assert.True(host.Left + host.Width < 1920);
        Assert.True(host.Left > 0);
    }

    [Fact]
    public void Chrome_PinsCloseAndCopyToActiveMonitorCorners()
    {
        var chrome = TranslateResultLayout.Chrome(RightMon, copyButtonWidth: 160);

        Assert.Equal(RightMon.Right - 60, chrome.CloseLeft);
        Assert.Equal(RightMon.Top + 20, chrome.CloseTop);
        Assert.Equal(RightMon.Right - 160 - 24, chrome.CopyLeft);
        Assert.Equal(RightMon.Bottom - 64, chrome.CopyTop);
        Assert.True(chrome.CloseLeft > 1920);
        Assert.True(chrome.CopyLeft > 1920);
    }

    [Fact]
    public void Zoom_StaysOnActiveMonitor()
    {
        var host = TranslateResultLayout.Host(RightMon, imgW: 400, imgH: 300, selW: 200, selH: 150);
        var zoom = TranslateResultLayout.Zoom(RightMon, host.Width, host.Height, zoomed: true);

        Assert.InRange(zoom.Scale, 1.0, 1.35);
        Assert.True(zoom.Left >= RightMon.Left);
        Assert.True(zoom.Left + host.Width <= RightMon.Right + 1);
        Assert.True(zoom.Top >= RightMon.Top);
        Assert.True(zoom.Top + host.Height <= RightMon.Bottom + 1);
    }
}
