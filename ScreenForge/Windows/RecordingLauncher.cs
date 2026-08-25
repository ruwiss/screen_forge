using System.IO;
using System.Windows;
using ScreenForge.Gif;
using ScreenForge.Record;
using ScreenForge.Settings;

namespace ScreenForge.Windows;

internal static class RecordingLauncher
{
    public static void StartGif(AppSettings settings, System.Drawing.Rectangle pixelRegion, Rect dipRegion)
    {
        if (pixelRegion.Width <= 0 || pixelRegion.Height <= 0)
            return;

        long maxBytes = Math.Max(32, settings.Gif.MaxFrameMemoryMb) * 1024L * 1024L;
        var recorder = new GifRecorder(pixelRegion, fps: settings.Gif.Fps, maxFrameBytes: maxBytes)
        {
            CaptureCursor = settings.Gif.CaptureCursor,
            TrackMouse = settings.Gif.HighlightClicks || settings.Gif.HighlightCursor,
            TrackKeyboard = settings.Gif.ShowKeys,
        };

        var overlay = new RecordingOverlayWindow(recorder, dipRegion, RecordingKind.Gif);
        overlay.Stopped += session =>
        {
            if (session is GifRecorder gif)
                new GifEditorWindow(gif, settings).Show();
        };
        overlay.Show();
        recorder.Start();
    }

    public static void StartVideo(AppSettings settings, System.Drawing.Rectangle pixelRegion, Rect dipRegion)
    {
        var (w, h) = VideoGeometry.EvenSize(pixelRegion.Width, pixelRegion.Height);
        if (w == 0)
        {
            MessageBox.Show("Kayıt alanı çok küçük", "ScreenForge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        pixelRegion.Width = w;
        pixelRegion.Height = h;

        var mixer = new AudioMixer(
            settings.Video.RecordSystemAudio,
            settings.Video.RecordMicrophone,
            settings.Video.MicDeviceId);

        if (settings.Video.ShowCountdown &&
            !RecordingCountdown.Run(dipRegion, settings, mixer))
        {
            mixer.Dispose();
            return;
        }

        var rec = new VideoRecorder(pixelRegion, settings.Video, mixer);
        var overlay = new RecordingOverlayWindow(rec, dipRegion, RecordingKind.Video);
        overlay.Stopped += session =>
        {
            if (session is VideoRecorder video)
                PromptSave(video, settings);
        };
        overlay.Show();
        rec.Start();
    }

    private static void PromptSave(VideoRecorder rec, AppSettings settings)
    {
        if (rec.Failure != null)
        {
            TryDelete(rec.OutputPath);
            MessageBox.Show("Kayıt başarısız: " + rec.Failure.Message, "ScreenForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            rec.Dispose();
            return;
        }

        string? path = rec.OutputPath;
        rec.Dispose();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        string nice = Path.Combine(
            Path.GetDirectoryName(path) ?? Path.GetTempPath(),
            $"ScreenForge-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
        try
        {
            if (!string.Equals(path, nice, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(nice)) File.Delete(nice);
                File.Move(path, nice);
                path = nice;
            }
        }
        catch { /* guid adıyla devam */ }

        new VideoResultWindow(path, settings).Show();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
