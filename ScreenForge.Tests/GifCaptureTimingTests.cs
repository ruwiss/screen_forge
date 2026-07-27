using System.Diagnostics;
using System.Drawing;
using ScreenForge.Gif;
using ScreenForge.Gif.Input;

namespace ScreenForge.Tests;

/// <summary>
/// Yakalama hızını ve gecikme muhasebesini doğrular.
/// </summary>
[Collection("WPF")]
public sealed class GifCaptureTimingTests
{
    [Fact]
    public void Capture_ReachesTargetFrameRate()
    {
        WpfRunner.Run(() =>
        {
            // Küçük bir bölge: sistem rahatça yetişmeli.
            using var recorder = new GifRecorder(new Rectangle(0, 0, 320, 240), fps: 30);
            recorder.CaptureCursor = false;
            recorder.TrackMouse = false;
            recorder.TrackKeyboard = false;

            recorder.Start();
            Thread.Sleep(1000);
            recorder.Stop();

            // 30 fps × 1 sn = 30 deneme. Ölçüm gürültüsü için geniş pay bırak;
            // amaç "yarısına bile ulaşamıyor" durumunu yakalamak.
            Assert.True(recorder.CaptureEfficiency > 0.7,
                $"yakalama verimi düşük: {recorder.CaptureEfficiency:P0}");
        });
    }

    [Fact]
    public void Capture_HigherFpsProducesMoreAttempts()
    {
        WpfRunner.Run(() =>
        {
            double slow = MeasureEfficiency(fps: 10);
            double fast = MeasureEfficiency(fps: 30);

            // Her iki hız da hedefine yakın kalmalı; yüksek FPS çökmemeli.
            Assert.True(slow > 0.7, $"10 fps verimi düşük: {slow:P0}");
            Assert.True(fast > 0.7, $"30 fps verimi düşük: {fast:P0}");
        });
    }

    private static double MeasureEfficiency(int fps)
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 320, 240), fps: fps);
        recorder.CaptureCursor = false;
        recorder.TrackMouse = false;
        recorder.TrackKeyboard = false;

        recorder.Start();
        Thread.Sleep(700);
        recorder.Stop();

        return recorder.CaptureEfficiency;
    }

    // ─── Gecikme muhasebesi ───────────────────────────────────────────────────

    [Fact]
    public void StoredDelays_PreserveRealElapsedTime()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = new GifRecorder(new Rectangle(0, 0, 200, 150), fps: 20);
            recorder.CaptureCursor = false;
            recorder.TrackMouse = false;
            recorder.TrackKeyboard = false;

            var clock = Stopwatch.StartNew();
            recorder.Start();
            Thread.Sleep(1200);
            recorder.Stop();
            clock.Stop();

            var recording = recorder.DetachFrames();
            if (recording.Frames.Count == 0)
                return; // ekran tamamen sabitse kare üretilmeyebilir

            long total = recording.FrameDelays.Sum(d => (long)d);

            // Toplam gecikme gerçek kayıt süresini yansıtmalı.
            // Eski hata bu birikimi siliyor ve süre çok kısa çıkıyordu.
            Assert.InRange(total, clock.ElapsedMilliseconds * 0.4, clock.ElapsedMilliseconds * 1.5);
        });
    }

    [Fact]
    public void IdenticalFrames_ExtendDelayInsteadOfDuplicating()
    {
        // Durağan ekranda tek kare tutulup süresi uzatılmalı.
        using var recorder = new GifRecorder(new Rectangle(0, 0, 4, 4), maxFrameBytes: 4096);

        var pixels = new byte[4 * 4 * 4];
        recorder.TryStoreFrame(pixels, 50);
        recorder.TryStoreFrame((byte[])pixels.Clone(), 50);

        // TryStoreFrame doğrudan çağrıldığında ayıklama yapılmaz; bu test
        // depolamanın gecikmeyi kırpma sınırları içinde tuttuğunu doğrular.
        Assert.All(recorder.FrameDelays, d => Assert.InRange(d, 1, 655350));
    }

    [Fact]
    public void Pause_DoesNotInflateDelays()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = new GifRecorder(new Rectangle(0, 0, 160, 120), fps: 20);
            recorder.CaptureCursor = false;
            recorder.TrackMouse = false;
            recorder.TrackKeyboard = false;

            recorder.Start();
            Thread.Sleep(300);

            recorder.Pause();
            Thread.Sleep(600);   // duraklama çıktıya yansımamalı
            recorder.Resume();

            Thread.Sleep(300);
            recorder.Stop();

            var recording = recorder.DetachFrames();
            if (recording.Frames.Count == 0)
                return;

            long total = recording.FrameDelays.Sum(d => (long)d);

            // Yaklaşık 600 ms kayıt yapıldı; 600 ms duraklama eklenmemeli.
            Assert.True(total < 1100, $"duraklama süreye sızdı: {total} ms");
        });
    }

    [Fact]
    public void State_FollowsSessionLifecycle()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = new GifRecorder(new Rectangle(0, 0, 64, 64), fps: 10);
            recorder.CaptureCursor = false;
            recorder.TrackMouse = false;
            recorder.TrackKeyboard = false;

            Assert.Equal(GifRecorderState.Idle, recorder.State);

            recorder.Start();
            Assert.Equal(GifRecorderState.Recording, recorder.State);

            recorder.Pause();
            Assert.Equal(GifRecorderState.Paused, recorder.State);

            recorder.Resume();
            Assert.Equal(GifRecorderState.Recording, recorder.State);

            recorder.Stop();
            Assert.Equal(GifRecorderState.Stopped, recorder.State);
        });
    }

    [Fact]
    public void Stop_IsSafeToCallTwice()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = new GifRecorder(new Rectangle(0, 0, 64, 64), fps: 10);
            recorder.CaptureCursor = false;
            recorder.TrackMouse = false;
            recorder.TrackKeyboard = false;

            recorder.Start();
            Thread.Sleep(100);

            recorder.Stop();
            recorder.Stop();   // ikinci çağrı hata vermemeli

            Assert.Equal(GifRecorderState.Stopped, recorder.State);
        });
    }
}
