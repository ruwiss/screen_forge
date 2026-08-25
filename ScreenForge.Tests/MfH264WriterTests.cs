using ScreenForge.Record;

namespace ScreenForge.Tests;

public sealed class MfH264WriterTests
{
    [Fact]
    public void WritesNonEmptyMp4()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sf-h264-{Guid.NewGuid():N}.mp4");
        try
        {
            const int w = 320, h = 240;
            var pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 80;
                pixels[i + 1] = 40;
                pixels[i + 2] = 200;
                pixels[i + 3] = 255;
            }

            using (var writer = new MfH264Writer(path, w, h, 10, 1_000_000))
            {
                writer.WriteBgra(pixels, 0);
                writer.WriteBgra(pixels, 1_000_000);
                writer.Finish();
            }

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 200);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
