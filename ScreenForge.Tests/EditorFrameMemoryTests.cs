using System.Windows.Media.Imaging;
using ScreenForge.Gif;
using ScreenForge.Gif.Editing;
using ScreenForge.Gif.Input;

namespace ScreenForge.Tests;

public sealed class EditorFrameMemoryTests
{
    [Fact]
    public void FromPacked_DoesNotDecodeUntilPixelsAccessed()
    {
        var pixels = MakePixels(8, 8, 40);
        var packed = FrameStore.Compress(pixels);

        var frame = EditorFrame.FromPacked(packed, pixels.Length, 100, new FrameInput());

        Assert.False(frame.IsDecoded);
        Assert.Equal(pixels, frame.Pixels);
        Assert.True(frame.IsDecoded);

        frame.ReleaseDecoded();
        Assert.False(frame.IsDecoded);
        Assert.Equal(pixels, frame.Pixels);
    }

    [Fact]
    public void WithPixels_DoesNotKeepDecodedCopy()
    {
        var frame = new EditorFrame(MakePixels(4, 4, 10), 100);
        var cropped = frame.WithPixels(MakePixels(2, 2, 20));

        Assert.False(cropped.IsDecoded);
        Assert.Equal(2 * 2 * 4, cropped.RawLength);
    }

    [Fact]
    public void ThumbnailCache_DoesNotDecodeFrame()
    {
        WpfRunner.Run(() =>
        {
            var pixels = MakePixels(32, 24, 80);
            var frame = EditorFrame.FromPacked(FrameStore.Compress(pixels), pixels.Length, 80, new FrameInput());
            var cache = new ThumbnailCache(targetWidth: 8);
            cache.SetFrameSize(32, 24);

            var thumb = (BitmapSource)cache.Get(frame);

            Assert.False(frame.IsDecoded);
            Assert.Equal(8, thumb.PixelWidth);
            Assert.Equal(6, thumb.PixelHeight);
        });
    }

    [Fact]
    public void DetachPacked_LeavesFramesCompressed()
    {
        using var recorder = new GifRecorder(new System.Drawing.Rectangle(0, 0, 8, 8));
        var original = MakePixels(8, 8, 90);
        recorder.TryStoreFrame(original, 100);

        var recording = recorder.DetachPacked();

        Assert.Single(recording.Packed);
        Assert.True(recording.Packed[0].Length < original.Length);
        Assert.Equal(original, FrameStore.Decompress(recording.Packed[0], recording.FrameByteCount));
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
}
