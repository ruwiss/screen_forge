using System.Drawing;
using ScreenForge.Gif;

namespace ScreenForge.Tests;

public sealed class GifRecorderTests
{
    [Fact]
    public void TryStoreFrame_StopsAcceptingFramesAtMemoryLimit()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 1, 1), maxFrameBytes: 8);

        Assert.True(recorder.TryStoreFrame(new byte[4], 100));
        Assert.True(recorder.TryStoreFrame(new byte[4], 100));
        Assert.False(recorder.TryStoreFrame(new byte[1], 100));

        Assert.True(recorder.MemoryLimitReached);
        Assert.Equal(8, recorder.FrameBytes);
        Assert.Equal(2, recorder.FrameCount);
    }

    [Fact]
    public void DetachFrames_TransfersFrameOwnership()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 1, 1), maxFrameBytes: 16);
        recorder.TryStoreFrame(new byte[4], 100);

        var recording = recorder.DetachFrames();

        Assert.Single(recording.Frames);
        Assert.Single(recording.FrameDelays);
        Assert.Empty(recorder.Frames);
        Assert.Empty(recorder.FrameDelays);
        Assert.Equal(0, recorder.FrameBytes);
    }
}
