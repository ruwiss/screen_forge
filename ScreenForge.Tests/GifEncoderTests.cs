using System.Drawing;
using ScreenForge.Gif;
using ScreenForge.Gif.Encoder;
using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Tests;

public sealed class GifEncoderTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(64, 5)]
    [InlineData(256, 7)]
    public void SizeField_PicksSmallestPowerOfTwoThatFits(int colorCount, int expected)
        => Assert.Equal(expected, GifFile.SizeField(colorCount));

    [Fact]
    public void PaletteMap_FindsNearestColor()
    {
        var palette = new List<WpfColor>
        {
            WpfColor.FromRgb(0, 0, 0),
            WpfColor.FromRgb(255, 0, 0),
            WpfColor.FromRgb(0, 255, 0),
        };
        var map = new PaletteMap(palette);

        Assert.Equal(0, map.Map(10, 10, 10));
        Assert.Equal(1, map.Map(240, 12, 8));
        Assert.Equal(2, map.Map(8, 230, 12));
    }

    [Fact]
    public void PaletteMap_NeverReturnsTransparentSlot()
    {
        var palette = new List<WpfColor>
        {
            WpfColor.FromRgb(255, 255, 255),
            WpfColor.FromRgb(0, 0, 0), // saydam slot
        };
        var map = new PaletteMap(palette, transparentIndex: 1);

        // Siyahın tam eşi saydam slot olsa da opak piksel oraya düşmemeli.
        Assert.Equal(0, map.Map(0, 0, 0));
        Assert.Equal(1, map.TransparentIndex);
    }

    [Fact]
    public void OctreeQuantizer_ProducesPaletteWithinLimit()
    {
        var pixels = MakeGradient(32, 32);
        var quantizer = new OctreeQuantizer { MaxColors = 16 };

        var palette = quantizer.Quantize(pixels);

        Assert.NotEmpty(palette);
        Assert.True(palette.Count <= 16, $"palet {palette.Count} renk döndürdü, sınır 16");
    }

    [Fact]
    public void NeuralQuantizer_ProducesRequestedPaletteSize()
    {
        var pixels = MakeGradient(32, 32);
        var quantizer = new NeuralQuantizer(samplingFactor: 1, maximumColors: 32);

        var palette = quantizer.Quantize(pixels);

        Assert.Equal(32, palette.Count);
        Assert.All(palette, c => Assert.InRange(c.R, (byte)0, (byte)255));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(64)]
    public async Task SaveAsync_WritesValidGifHeaderAndTrailer(int colorCount)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            await WriteSampleGifAsync(path, new GifExportOptions { ColorCount = colorCount });

            var bytes = await File.ReadAllBytesAsync(path);

            Assert.True(bytes.Length > 16);
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
            Assert.Equal(0x3b, bytes[^1]); // trailer
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_LogicalScreenMatchesRequestedSize()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            await WriteSampleGifAsync(path, new GifExportOptions());

            var bytes = await File.ReadAllBytesAsync(path);
            int width = bytes[6] | (bytes[7] << 8);
            int height = bytes[8] | (bytes[9] << 8);

            Assert.Equal(8, width);
            Assert.Equal(8, height);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_OptimizedOutputIsSmallerThanUnoptimized()
    {
        string optimized = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");
        string plain = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            var frames = MakeMostlyStaticFrames(64, 64, count: 12);

            await WriteGifAsync(optimized, frames, 64, 64,
                new GifExportOptions { Frames = frames, OptimizeUnchangedPixels = true });
            await WriteGifAsync(plain, frames, 64, 64,
                new GifExportOptions { Frames = frames, OptimizeUnchangedPixels = false });

            long optimizedSize = new FileInfo(optimized).Length;
            long plainSize = new FileInfo(plain).Length;

            Assert.True(optimizedSize < plainSize,
                $"optimize edilmiş {optimizedSize} bayt, düz {plainSize} bayttan küçük olmalı");
        }
        finally
        {
            TryDelete(optimized);
            TryDelete(plain);
        }
    }

    [Fact]
    public async Task SaveAsync_GlobalPaletteEmitsSinglePalette()
    {
        string global = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");
        string local = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            var frames = MakeMostlyStaticFrames(64, 64, count: 10);

            await WriteGifAsync(global, frames, 64, 64,
                new GifExportOptions { Frames = frames, UseGlobalPalette = true, OptimizeUnchangedPixels = false });
            await WriteGifAsync(local, frames, 64, 64,
                new GifExportOptions { Frames = frames, UseGlobalPalette = false, OptimizeUnchangedPixels = false });

            var globalBytes = await File.ReadAllBytesAsync(global);

            // Global palet bayrağı: logical screen descriptor packed alanının 7. biti
            Assert.True((globalBytes[10] & 0x80) != 0);
            Assert.True(new FileInfo(global).Length < new FileInfo(local).Length);
        }
        finally
        {
            TryDelete(global);
            TryDelete(local);
        }
    }

    [Fact]
    public async Task SaveAsync_CancellationLeavesNoPartialFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var frames = MakeMostlyStaticFrames(32, 32, count: 4);
            using var recorder = new GifRecorder(new Rectangle(0, 0, 32, 32));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                recorder.SaveAsync(path, new GifExportOptions { Frames = frames }, cancellationToken: cts.Token));

            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + ".tmp");
        }
    }

    [Fact]
    public async Task SaveAsync_DitheringProducesReadableGif()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            var frames = new List<byte[]> { MakeGradient(48, 48) };
            await WriteGifAsync(path, frames, 48, 48,
                new GifExportOptions { Frames = frames, Dithering = true, ColorCount = 32 });

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
            Assert.Equal(0x3b, bytes[^1]);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static Task WriteSampleGifAsync(string path, GifExportOptions options)
    {
        var frames = MakeMostlyStaticFrames(8, 8, count: 3);
        return WriteGifAsync(path, frames, 8, 8, new GifExportOptions
        {
            Frames = frames,
            ColorCount = options.ColorCount,
            Dithering = options.Dithering,
            UseGlobalPalette = options.UseGlobalPalette,
            OptimizeUnchangedPixels = options.OptimizeUnchangedPixels,
        });
    }

    private static async Task WriteGifAsync(string path, List<byte[]> frames, int width, int height, GifExportOptions options)
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, width, height));
        await recorder.SaveAsync(path, options);
    }

    private static byte[] MakeGradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)((x + y) * 127 / Math.Max(1, width + height - 2));
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>Yalnızca küçük bir karesi değişen kare dizisi — delta optimizasyonu için tipik senaryo.</summary>
    private static List<byte[]> MakeMostlyStaticFrames(int width, int height, int count)
    {
        var baseFrame = MakeGradient(width, height);
        var frames = new List<byte[]>(count);

        for (int f = 0; f < count; f++)
        {
            var frame = (byte[])baseFrame.Clone();
            int px = f % Math.Max(1, width - 2);
            int py = f % Math.Max(1, height - 2);

            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int i = ((py + dy) * width + px + dx) * 4;
                    frame[i] = 0;
                    frame[i + 1] = 0;
                    frame[i + 2] = 255;
                    frame[i + 3] = 255;
                }
            }

            frames.Add(frame);
        }

        return frames;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* geçici dosya */ }
    }
}
