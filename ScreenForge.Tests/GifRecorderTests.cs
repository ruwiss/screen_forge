using System.Drawing;
using System.Windows;
using ScreenForge.Gif;

namespace ScreenForge.Tests;

public sealed class GifRecorderTests
{
    [Fact]
    public void TryStoreFrame_StopsAcceptingFramesAtMemoryLimit()
    {
        // Sınır sıkıştırılmış boyuta uygulanır; küçük bir bütçe hızla dolar.
        using var recorder = new GifRecorder(new Rectangle(0, 0, 16, 16), maxFrameBytes: 40);

        int stored = 0;
        for (int i = 0; i < 50; i++)
        {
            // Her kare farklı olsun ki gerçekten yer kaplasın.
            if (!recorder.TryStoreFrame(MakeNoisyFrame(16, 16, seed: i), 100))
                break;

            stored++;
        }

        Assert.True(recorder.MemoryLimitReached);
        Assert.Equal(stored, recorder.FrameCount);
        Assert.True(recorder.FrameBytes <= 40, $"bütçe aşıldı: {recorder.FrameBytes}");
    }

    [Fact]
    public void MemoryUsageRatio_GrowsAsFramesAreStored()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 32, 32), maxFrameBytes: 64 * 1024);

        Assert.Equal(0, recorder.MemoryUsageRatio);

        recorder.TryStoreFrame(MakeNoisyFrame(32, 32, seed: 1), 100);
        double afterFirst = recorder.MemoryUsageRatio;
        Assert.True(afterFirst > 0);

        recorder.TryStoreFrame(MakeNoisyFrame(32, 32, seed: 2), 100);
        Assert.True(recorder.MemoryUsageRatio > afterFirst);
    }

    [Fact]
    public void StoredFrames_SurviveCompressionRoundTrip()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 24, 24), maxFrameBytes: 1024 * 1024);

        var original = MakeNoisyFrame(24, 24, seed: 7);
        recorder.TryStoreFrame(original, 100);

        var recording = recorder.DetachFrames();

        // Sıkıştırma kayıpsız olmalı; aksi hâlde çıktı bozulur.
        Assert.Single(recording.Frames);
        Assert.Equal(original, recording.Frames[0]);
    }

    [Fact]
    public void CompressionRatio_ExceedsOneForRepetitiveContent()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 64, 64), maxFrameBytes: 8 * 1024 * 1024);

        // Düz renk kare — ekran görüntüsü içeriğine benzer şekilde iyi sıkışır.
        recorder.TryStoreFrame(new byte[64 * 64 * 4], 100);

        Assert.True(recorder.CompressionRatio > 5,
            $"sıkıştırma beklenenden zayıf: {recorder.CompressionRatio:0.0}x");
        Assert.True(recorder.FrameBytes < recorder.UncompressedBytes);
    }

    [Fact]
    public void DetachFrames_TransfersFrameOwnership()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 8, 8), maxFrameBytes: 1024 * 1024);
        recorder.TryStoreFrame(new byte[8 * 8 * 4], 100);

        var recording = recorder.DetachFrames();

        Assert.Single(recording.Frames);
        Assert.Single(recording.FrameDelays);
        Assert.Equal(0, recorder.FrameCount);
        Assert.Empty(recorder.FrameDelays);
        Assert.Equal(0, recorder.FrameBytes);
    }

    /// <summary>Sıkışmaya direnen, her çağrıda farklı kare üretir.</summary>
    private static byte[] MakeNoisyFrame(int width, int height, int seed)
    {
        var frame = new byte[width * height * 4];
        var random = new Random(seed);
        random.NextBytes(frame);

        for (int i = 3; i < frame.Length; i += 4) frame[i] = 255;
        return frame;
    }

    [Fact]
    public void Pause_And_Resume_MoveThroughExpectedStates()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 1, 1));

        Assert.Equal(GifRecorderState.Idle, recorder.State);

        // Pause/Resume yalnızca ilgili durumdan geçiş yapar.
        recorder.Pause();
        Assert.Equal(GifRecorderState.Idle, recorder.State);

        recorder.Resume();
        Assert.Equal(GifRecorderState.Idle, recorder.State);
    }

    [Fact]
    public void FindChangedBounds_ReturnsEmptyForIdenticalFrames()
    {
        var frame = MakeFrame(4, 4, 0x20);

        var bounds = GifRecorder.FindChangedBounds(frame, (byte[])frame.Clone(), 4, 4);

        Assert.True(bounds.IsEmpty);
    }

    [Fact]
    public void FindChangedBounds_TightlyBoundsChangedPixels()
    {
        var previous = MakeFrame(8, 8, 0x10);
        var current = (byte[])previous.Clone();
        SetPixel(current, 8, x: 3, y: 2, value: 0xFF);
        SetPixel(current, 8, x: 5, y: 6, value: 0xFF);

        var bounds = GifRecorder.FindChangedBounds(previous, current, 8, 8);

        Assert.Equal(new Int32Rect(3, 2, 3, 5), bounds);
    }

    [Fact]
    public void FindChangedBounds_HonoursTolerance()
    {
        var previous = MakeFrame(4, 4, 0x40);
        var current = (byte[])previous.Clone();
        SetPixel(current, 4, x: 1, y: 1, value: 0x44); // kanal başına 4 fark

        Assert.False(GifRecorder.FindChangedBounds(previous, current, 4, 4).IsEmpty);
        Assert.True(GifRecorder.FindChangedBounds(previous, current, 4, 4, tolerance: 8).IsEmpty);
    }

    [Fact]
    public void CropAndMask_MarksUnchangedPixelsTransparent()
    {
        var previous = MakeFrame(4, 4, 0x30);
        var current = (byte[])previous.Clone();
        SetPixel(current, 4, x: 1, y: 1, value: 0x90);
        SetPixel(current, 4, x: 2, y: 2, value: 0x90);

        var rect = GifRecorder.FindChangedBounds(previous, current, 4, 4);
        var (pixels, hasTransparency) = GifRecorder.CropAndMask(previous, current, 4, rect);

        Assert.Equal(new Int32Rect(1, 1, 2, 2), rect);
        Assert.True(hasTransparency);

        // (1,1) değişti → opak; (2,1) değişmedi → alfa 0
        Assert.Equal(255, pixels[3]);
        Assert.Equal(0, pixels[7]);
    }

    [Fact]
    public void BuildFramesForExport_MergesDelayOfIdenticalFrames()
    {
        var frame = MakeFrame(2, 2, 0x11);
        var frames = new List<byte[]> { frame, (byte[])frame.Clone(), (byte[])frame.Clone() };
        var delays = new List<int> { 100, 100, 100 };

        var plan = GifRecorder.BuildFramesForExport(frames, delays, 2, 2, 100, optimize: true);

        Assert.Single(plan);
        Assert.Equal(300, plan[0].Delay);
    }

    [Fact]
    public void BuildFramesForExport_EmitsFullFirstFrameThenDeltas()
    {
        var first = MakeFrame(4, 4, 0x10);
        var second = (byte[])first.Clone();
        SetPixel(second, 4, x: 2, y: 2, value: 0xEE);

        var plan = GifRecorder.BuildFramesForExport(
            new List<byte[]> { first, second }, new List<int> { 100, 100 }, 4, 4, 100, optimize: true);

        Assert.Equal(2, plan.Count);
        Assert.Equal(new Int32Rect(0, 0, 4, 4), plan[0].Rect);
        Assert.False(plan[0].HasTransparency);
        Assert.Equal(new Int32Rect(2, 2, 1, 1), plan[1].Rect);
    }

    [Fact]
    public void BuildFramesForExport_WithoutOptimizeKeepsFullFrames()
    {
        var first = MakeFrame(4, 4, 0x10);
        var second = (byte[])first.Clone();
        SetPixel(second, 4, x: 2, y: 2, value: 0xEE);

        var plan = GifRecorder.BuildFramesForExport(
            new List<byte[]> { first, second }, new List<int> { 100, 100 }, 4, 4, 100, optimize: false);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, f => Assert.Equal(new Int32Rect(0, 0, 4, 4), f.Rect));
        Assert.All(plan, f => Assert.False(f.HasTransparency));
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static byte[] MakeFrame(int width, int height, byte value)
    {
        var frame = new byte[width * height * 4];
        for (int i = 0; i < frame.Length; i += 4)
        {
            frame[i] = value;
            frame[i + 1] = value;
            frame[i + 2] = value;
            frame[i + 3] = 255;
        }
        return frame;
    }

    private static void SetPixel(byte[] frame, int width, int x, int y, byte value)
    {
        int offset = (y * width + x) * 4;
        frame[offset] = value;
        frame[offset + 1] = value;
        frame[offset + 2] = value;
        frame[offset + 3] = 255;
    }
}
