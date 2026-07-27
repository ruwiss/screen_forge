using System.Drawing;
using System.Drawing.Imaging;
using ScreenForge.Gif;

namespace ScreenForge.Tests;

/// <summary>
/// Yazılan GIF'i sistem kod çözücüsüyle geri okuyup piksellerin doğruluğunu kontrol eder.
/// Başlık testlerinden farkı: delta kare + saydamlık mantığının gerçekten
/// doğru görüntü ürettiğini kanıtlar.
/// </summary>
public sealed class GifRoundTripTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DecodedFrames_MatchSourceWithinQuantizationError(bool optimize, bool globalPalette)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            const int Size = 48;
            var frames = MakeMovingBlockFrames(Size, Size, count: 5);

            using (var recorder = new GifRecorder(new Rectangle(0, 0, Size, Size)))
            {
                await recorder.SaveAsync(path, new GifExportOptions
                {
                    Frames = frames,
                    FrameDelays = Enumerable.Repeat(100, frames.Count).ToList(),
                    Width = Size,
                    Height = Size,
                    ColorCount = 256,
                    OptimizeUnchangedPixels = optimize,
                    UseGlobalPalette = globalPalette,
                });
            }

            using var image = Image.FromFile(path);
            var dimension = new FrameDimension(image.FrameDimensionsList[0]);
            int decodedCount = image.GetFrameCount(dimension);

            Assert.Equal(frames.Count, decodedCount);
            Assert.Equal(Size, image.Width);
            Assert.Equal(Size, image.Height);

            for (int f = 0; f < decodedCount; f++)
            {
                image.SelectActiveFrame(dimension, f);
                using var bitmap = new Bitmap(image);

                AssertFrameMatches(bitmap, frames[f], Size, Size, frameIndex: f);
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task IdenticalFrames_CollapseIntoSingleLongerFrame()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            var frame = MakeSolid(16, 16, 40, 90, 160);
            var frames = new List<byte[]> { frame, (byte[])frame.Clone(), (byte[])frame.Clone() };

            using (var recorder = new GifRecorder(new Rectangle(0, 0, 16, 16)))
            {
                await recorder.SaveAsync(path, new GifExportOptions
                {
                    Frames = frames,
                    FrameDelays = new List<int> { 100, 100, 100 },
                    Width = 16,
                    Height = 16,
                    OptimizeUnchangedPixels = true,
                });
            }

            using var image = Image.FromFile(path);
            var dimension = new FrameDimension(image.FrameDimensionsList[0]);

            // Üç aynı kare tek bloğa iner; süresi toplanır.
            Assert.Equal(1, image.GetFrameCount(dimension));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task LowColorCount_StillDecodesToCloseColors()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            // 64 renk → LZW başlangıç kod boyutu 6 bit olmalı; çözücü yine de doğru okumalı.
            var frames = new List<byte[]> { MakeSolid(24, 24, 200, 60, 30) };

            using (var recorder = new GifRecorder(new Rectangle(0, 0, 24, 24)))
            {
                await recorder.SaveAsync(path, new GifExportOptions
                {
                    Frames = frames,
                    Width = 24,
                    Height = 24,
                    ColorCount = 64,
                    OptimizeUnchangedPixels = false,
                });
            }

            using var image = Image.FromFile(path);
            using var bitmap = new Bitmap(image);
            var pixel = bitmap.GetPixel(12, 12);

            Assert.InRange(pixel.R, 180, 220);
            Assert.InRange(pixel.G, 40, 80);
            Assert.InRange(pixel.B, 10, 50);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ─── Doğrulama ────────────────────────────────────────────────────────────

    private static void AssertFrameMatches(Bitmap decoded, byte[] expectedBgra, int width, int height, int frameIndex)
    {
        // Kuantalama hatası payı: 256 renkte sentetik içerik için fazlasıyla geniş.
        const int Tolerance = 40;

        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                int i = (y * width + x) * 4;
                var actual = decoded.GetPixel(x, y);

                int db = Math.Abs(actual.B - expectedBgra[i]);
                int dg = Math.Abs(actual.G - expectedBgra[i + 1]);
                int dr = Math.Abs(actual.R - expectedBgra[i + 2]);

                Assert.True(db <= Tolerance && dg <= Tolerance && dr <= Tolerance,
                    $"kare {frameIndex} ({x},{y}): beklenen " +
                    $"BGR({expectedBgra[i]},{expectedBgra[i + 1]},{expectedBgra[i + 2]}) " +
                    $"gelen BGR({actual.B},{actual.G},{actual.R})");
            }
        }
    }

    // ─── Örnek veri ───────────────────────────────────────────────────────────

    private static byte[] MakeSolid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    /// <summary>Sabit arka plan üzerinde hareket eden bir blok — delta kodlamanın klasik testi.</summary>
    private static List<byte[]> MakeMovingBlockFrames(int width, int height, int count)
    {
        var frames = new List<byte[]>(count);

        for (int f = 0; f < count; f++)
        {
            var frame = MakeSolid(width, height, 30, 60, 90);
            int left = f * 6;

            for (int y = 8; y < 20; y++)
            {
                for (int x = left; x < Math.Min(left + 12, width); x++)
                {
                    int i = (y * width + x) * 4;
                    frame[i] = 20;       // B
                    frame[i + 1] = 200;  // G
                    frame[i + 2] = 240;  // R
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
