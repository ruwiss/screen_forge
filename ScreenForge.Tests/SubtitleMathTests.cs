using System.Drawing;
using ScreenForge.Subtitle;

namespace ScreenForge.Tests;

public sealed class SubtitleMathTests
{
    [Fact]
    public void TargetLanguage_Empty_UsesTurkish()
    {
        Assert.Equal("tr", SubtitleMath.TargetLanguage(null));
        Assert.Equal("tr", SubtitleMath.TargetLanguage("  "));
        Assert.Equal("en", SubtitleMath.TargetLanguage(" en "));
    }

    [Fact]
    public void NormalizeOcr_CollapsesBlankLines()
    {
        Assert.Equal("Merhaba\nDünya", SubtitleMath.NormalizeOcr("  Merhaba  \r\n\n Dünya "));
        Assert.Null(SubtitleMath.NormalizeOcr("   \n"));
    }

    [Fact]
    public void Fingerprint_ChangesWhenSampledPixelChanges()
    {
        var a = new byte[16 * 8 * 4];
        var b = (byte[])a.Clone();
        b[0] = 255;
        Assert.NotEqual(0UL, SubtitleMath.Fingerprint(a, 16, 8));
        Assert.NotEqual(SubtitleMath.Fingerprint(a, 16, 8), SubtitleMath.Fingerprint(b, 16, 8));
    }

    [Fact]
    public void DefaultBox_MovesAboveSourceNearBottom()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var source = new Rectangle(400, 1000, 600, 60);
        var box = SubtitleMath.DefaultBox(source, screen);
        Assert.True(box.Bottom <= source.Y);
        Assert.True(box.Width >= 280);
    }

    [Fact]
    public void NearlySame_IgnoresOneCharacterJitter()
    {
        Assert.True(SubtitleMath.NearlySame("Hello world", "Hello wor1d"));
        Assert.False(SubtitleMath.NearlySame("Hello world", "Tamamen baska"));
    }

    [Fact]
    public void ChangedSamples_SkipsTinyNoise()
    {
        var a = new byte[SubtitleMath.SampleCount];
        var b = (byte[])a.Clone();
        b[0] = 10;
        Assert.Equal(0, SubtitleMath.ChangedSamples(a, b));
        b[3] = 80;
        Assert.Equal(1, SubtitleMath.ChangedSamples(a, b));
    }

    [Fact]
    public void PushOutside_SlidesOffBlockedEdge()
    {
        var blocked = new Rectangle(100, 100, 200, 40);
        var overlapping = new Rectangle(80, 90, 50, 30);
        var cleared = SubtitleMath.PushOutside(overlapping, blocked);
        Assert.False(cleared.IntersectsWith(blocked));
        var far = new Rectangle(10, 10, 40, 20);
        Assert.Equal(far, SubtitleMath.PushOutside(far, blocked));
    }

    [Fact]
    public void OcrSize_ShrinksWideRegions()
    {
        var (w, h) = SubtitleMath.OcrSize(2560, 200);
        Assert.True(w <= 1280);
        Assert.True(h <= 240);
        Assert.Equal((800, 80), SubtitleMath.OcrSize(800, 80));
    }
}
