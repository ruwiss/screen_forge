using ScreenForge.Record;
using ScreenForge.Settings;

namespace ScreenForge.Tests;

public sealed class VideoSettingsTests
{
    [Fact]
    public void Deserialize_PreservesVideoSettings()
    {
        const string json = """
            {
              "Video": {
                "Fps": 60,
                "Quality": "High",
                "CaptureCursor": false,
                "RecordSystemAudio": false,
                "RecordMicrophone": true,
                "MicDeviceId": "mic-1"
              }
            }
            """;

        var settings = AppSettings.Deserialize(json);

        Assert.NotNull(settings);
        Assert.Equal(60, settings.Video.Fps);
        Assert.Equal(VideoQuality.High, settings.Video.Quality);
        Assert.False(settings.Video.CaptureCursor);
        Assert.False(settings.Video.RecordSystemAudio);
        Assert.True(settings.Video.RecordMicrophone);
        Assert.Equal("mic-1", settings.Video.MicDeviceId);
    }

    [Fact]
    public void Defaults_VideoIsMedium30FpsWithCursor()
    {
        var settings = new AppSettings();

        Assert.Equal(VideoQuality.High, settings.Video.Quality);
        Assert.True(settings.Video.CaptureCursor);
        Assert.True(settings.Video.RecordSystemAudio);
        Assert.False(settings.Video.RecordMicrophone);
        Assert.True(settings.Video.HighlightClicks);
        Assert.True(settings.Video.ShowCountdown);
    }

    [Fact]
    public void EvenSize_SnapsOddAndRejectsTiny()
    {
        Assert.Equal((1920, 1080), VideoGeometry.EvenSize(1920, 1080));
        Assert.Equal((1918, 1080), VideoGeometry.EvenSize(1919, 1081));
        Assert.Equal((0, 0), VideoGeometry.EvenSize(1, 10));
    }

    [Fact]
    public void CapLongEdge_Shrinks4kTo1440pClass()
    {
        Assert.Equal((2560, 1440), VideoGeometry.CapLongEdge(3840, 2160));
        Assert.Equal((1920, 1080), VideoGeometry.CapLongEdge(1920, 1080));
    }

    [Fact]
    public void BitsPerSecond_ScalesAndClamps()
    {
        Assert.Equal(8_000_000, VideoBitrate.BitsPerSecond(VideoQuality.Medium, 1920, 1080));
        Assert.Equal(2_000_000, VideoBitrate.BitsPerSecond(VideoQuality.Medium, 960, 540));
        Assert.Equal(40_000_000, VideoBitrate.BitsPerSecond(VideoQuality.High, 3840, 2160));
    }
}
