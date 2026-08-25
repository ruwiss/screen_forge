using ScreenForge.Settings;

namespace ScreenForge.Record;

public static class VideoBitrate
{
    public static int BitsPerSecond(VideoQuality quality, int width, int height)
    {
        int baseline = quality switch
        {
            VideoQuality.Low => 2_500_000,
            VideoQuality.High => 16_000_000,
            _ => 8_000_000,
        };

        double scale = (double)width * height / (1920.0 * 1080.0);
        int bits = (int)Math.Round(baseline * scale);
        return Math.Clamp(bits, 1_000_000, 40_000_000);
    }
}
