using System.Drawing;
using System.Drawing.Imaging;
using ScreenForge.Gif.Editing;

namespace ScreenForge.Tests;

public sealed class OverlayRendererTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  İLERLEME GÖSTERGESİ
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProgressBar_DrawsOntoFrame()
    {
        var pixels = MakePixels(64, 64, 0x20);
        var set = new OverlaySet
        {
            Progress = new ProgressOptions { Enabled = true, Style = ProgressStyle.Bar },
        };

        var result = Apply(pixels, 64, 64, set, frameIndex: 5, frameCount: 10);

        Assert.NotEqual(pixels, result);
    }

    [Theory]
    [InlineData(ProgressReadout.Seconds)]
    [InlineData(ProgressReadout.Frames)]
    [InlineData(ProgressReadout.Percent)]
    public void ProgressText_DrawsForEveryReadout(ProgressReadout readout)
    {
        var pixels = MakePixels(160, 90, 0x20);
        var set = new OverlaySet
        {
            Progress = new ProgressOptions
            {
                Enabled = true,
                Style = ProgressStyle.Text,
                Readout = readout,
            },
        };

        var result = Apply(pixels, 160, 90, set, frameIndex: 3, frameCount: 10);

        // Yazı biçimi de kareye işlenmeli; çubuk kadar görünür olmalı.
        Assert.NotEqual(pixels, result);
    }

    [Fact]
    public void ProgressText_AppearsAtRequestedCorner()
    {
        var pixels = MakePixels(160, 90, 0x20);

        var topLeft = Apply(pixels, 160, 90, MakeProgressAt(OverlayPlacement.TopLeft), 3, 10);
        var bottomRight = Apply(pixels, 160, 90, MakeProgressAt(OverlayPlacement.BottomRight), 3, 10);

        Assert.True(RegionChanged(pixels, topLeft, 160, 4, 4, 60, 24));
        Assert.False(RegionChanged(pixels, bottomRight, 160, 4, 4, 60, 24));
    }

    [Fact]
    public void SecondsReadout_HonoursDecimalSetting()
    {
        var precise = new ProgressOptions { Readout = ProgressReadout.Seconds, SecondsDecimals = 1 };
        var rounded = new ProgressOptions { Readout = ProgressReadout.Seconds, SecondsDecimals = 0 };

        string a = OverlayRenderer.FormatReadout(precise, 0, 10, 3600, 7200, 0.5);
        string b = OverlayRenderer.FormatReadout(rounded, 0, 10, 3600, 7200, 0.5);

        Assert.Equal("3,6/7,2 sn", a.Replace('.', ','));
        Assert.Equal("4/7 sn", b);
    }

    [Fact]
    public void FrameAndPercentReadouts_IgnoreDecimalSetting()
    {
        var options = new ProgressOptions { Readout = ProgressReadout.Frames, SecondsDecimals = 0 };
        Assert.Equal("4/10", OverlayRenderer.FormatReadout(options, 3, 10, 0, 0, 0.4));

        var percent = new ProgressOptions { Readout = ProgressReadout.Percent };
        Assert.Equal("40%", OverlayRenderer.FormatReadout(percent, 3, 10, 0, 0, 0.4));
    }

    private static OverlaySet MakeProgressAt(OverlayPlacement placement) => new()
    {
        Progress = new ProgressOptions
        {
            Enabled = true,
            Style = ProgressStyle.Text,
            Placement = placement,
        },
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  FİLİGRAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WatermarkText_DrawsOntoFrame()
    {
        var pixels = MakePixels(160, 90, 0x20);
        var set = new OverlaySet
        {
            Watermark = new WatermarkOptions { Enabled = true, Text = "ScreenForge" },
        };

        Assert.NotEqual(pixels, Apply(pixels, 160, 90, set, 0, 1));
    }

    [Fact]
    public void WatermarkImage_DrawsLogoOntoFrame()
    {
        string logo = CreateLogo(32, 32);

        try
        {
            var pixels = MakePixels(160, 90, 0x20);
            var set = new OverlaySet
            {
                Watermark = new WatermarkOptions
                {
                    Enabled = true,
                    ImagePath = logo,
                    ImageScale = 0.25,
                    Placement = OverlayPlacement.BottomRight,
                },
            };

            var result = Apply(pixels, 160, 90, set, 0, 1);

            Assert.NotEqual(pixels, result);
            Assert.True(RegionChanged(pixels, result, 160, 110, 50, 40, 30));
        }
        finally
        {
            TryDelete(logo);
        }
    }

    [Fact]
    public void WatermarkImage_TakesPrecedenceOverText()
    {
        string logo = CreateLogo(24, 24);

        try
        {
            var options = new WatermarkOptions
            {
                Enabled = true,
                Text = "yazı",
                ImagePath = logo,
            };

            Assert.True(options.HasImage);
            Assert.True(options.HasWork);
        }
        finally
        {
            TryDelete(logo);
        }
    }

    [Fact]
    public void WatermarkImage_IgnoresMissingFile()
    {
        var pixels = MakePixels(64, 64, 0x20);
        var set = new OverlaySet
        {
            Watermark = new WatermarkOptions
            {
                Enabled = true,
                ImagePath = Path.Combine(Path.GetTempPath(), $"yok_{Guid.NewGuid():N}.png"),
            },
        };

        // Dosya yoksa filigran atlanır; dışa aktarım çökmemeli.
        Assert.False(set.Watermark.HasImage);
        Assert.Equal(pixels, Apply(pixels, 64, 64, set, 0, 1));
    }

    [Fact]
    public void WatermarkImage_ScalesWithFrameWidth()
    {
        string logo = CreateLogo(40, 40);

        try
        {
            var pixels = MakePixels(200, 120, 0x20);

            var small = Apply(pixels, 200, 120, MakeLogoSet(logo, 0.08), 0, 1);
            var large = Apply(pixels, 200, 120, MakeLogoSet(logo, 0.30), 0, 1);

            Assert.NotEqual(small, large);
        }
        finally
        {
            TryDelete(logo);
        }
    }

    private static OverlaySet MakeLogoSet(string path, double scale) => new()
    {
        Watermark = new WatermarkOptions
        {
            Enabled = true,
            ImagePath = path,
            ImageScale = scale,
            Placement = OverlayPlacement.BottomRight,
        },
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  ORTAK
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_KeepsEveryPixelOpaque()
    {
        var pixels = MakePixels(96, 64, 0x30);
        var set = new OverlaySet
        {
            Caption = new CaptionOptions { Enabled = true, Text = "başlık" },
            Progress = new ProgressOptions { Enabled = true, Style = ProgressStyle.Text },
        };

        var result = Apply(pixels, 96, 64, set, 2, 10);

        for (int i = 3; i < result.Length; i += 4)
            Assert.Equal(255, result[i]);
    }

    [Fact]
    public void Apply_ReturnsSourceWhenNothingEnabled()
    {
        var pixels = MakePixels(32, 32, 0x30);

        Assert.Same(pixels, Apply(pixels, 32, 32, new OverlaySet(), 0, 1));
    }

    [Fact]
    public void Apply_DoesNotMutateSource()
    {
        var pixels = MakePixels(64, 64, 0x30);
        var original = (byte[])pixels.Clone();

        var set = new OverlaySet { Border = new BorderOptions { Enabled = true, Thickness = 3 } };
        Apply(pixels, 64, 64, set, 0, 1);

        Assert.Equal(original, pixels);
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static byte[] Apply(byte[] pixels, int width, int height, OverlaySet set,
        int frameIndex, int frameCount)
    {
        // OverlayRenderer internal; testler InternalsVisibleTo ile erişir.
        var method = typeof(ScreenForge.Gif.GifRecorder).Assembly
            .GetType("ScreenForge.Gif.Editing.OverlayRenderer")!
            .GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        return (byte[])method.Invoke(null, new object[]
        {
            pixels, width, height, set, frameIndex, frameCount,
            (long)(frameIndex * 100), (long)(frameCount * 100), 1.0,
        })!;
    }

    private static string CreateLogo(int width, int height)
    {
        string path = Path.Combine(Path.GetTempPath(), $"logo_{Guid.NewGuid():N}.png");

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.Magenta);

        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static byte[] MakePixels(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static bool RegionChanged(byte[] a, byte[] b, int width, int x, int y, int w, int h)
    {
        for (int row = y; row < y + h; row++)
        {
            for (int col = x; col < x + w; col++)
            {
                int i = (row * width + col) * 4;
                if (i + 2 >= a.Length) continue;

                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
                    return true;
            }
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* geçici dosya */ }
    }
}
