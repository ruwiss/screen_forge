using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ScreenForge.Capture;
using ScreenForge.Gif;
using ScreenForge.Gif.Input;
using ScreenForge.Settings;

namespace ScreenForge.Record;

public sealed class VideoRecorder : IRecordingSession
{
    private readonly Rectangle _region;
    private readonly VideoSettings _settings;
    private string _tempPath;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _gate = new();
    private readonly int _outWidth;
    private readonly int _outHeight;
    private readonly AudioMixer? _mixer;
    private readonly InputHook? _hook;

    private IFrameSource? _source;
    private MfH264Writer? _writer;
    private Thread? _thread;
    private ManualResetEventSlim? _resumeSignal;
    private volatile bool _running;
    private volatile bool _paused;
    private volatile bool _stopRequested;
    private long _graceUntilMs;
    private int _frameCount;
    private int _captureAttempts;
    private int _targetIntervalMs;
    private bool _disposed;
    private bool _finished;
    private long _audioBytes;
    private int _clickX, _clickY;
    private int _clickArgb;
    private long _clickUntil;

    internal VideoRecorder(Rectangle pixelRegion, VideoSettings settings, AudioMixer? mixer = null)
    {
        _region = pixelRegion;
        _settings = settings;
        (_outWidth, _outHeight) = VideoGeometry.CapLongEdge(pixelRegion.Width, pixelRegion.Height);
        Fps = Math.Clamp(settings.Fps, 1, 60);
        _targetIntervalMs = Math.Max(1, (int)Math.Round(1000.0 / Fps));
        _tempPath = Path.Combine(Path.GetTempPath(), $"screenforge-{Guid.NewGuid():N}.mp4");
        _mixer = mixer;
        if (settings.HighlightClicks)
        {
            _hook = new InputHook();
            _hook.MouseActivity += OnMouse;
            _hook.Start(mouse: true, keyboard: false);
        }
    }

    public string? OutputPath { get; private set; }
    public Exception? Failure { get; private set; }
    public int Fps { get; }
    public int FrameCount => _frameCount;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public bool IsPaused => _paused;
    public float SystemPeak => _mixer?.SystemPeak ?? 0;
    public float MicPeak => _mixer?.MicPeak ?? 0;
    public bool HasSystemAudio => _mixer?.HasSystem == true;
    public bool HasMic => _mixer?.HasMic == true;
    public double CaptureEfficiency
    {
        get
        {
            int attempts = _captureAttempts;
            if (attempts <= 0) return 1.0;
            double expected = _stopwatch.ElapsedMilliseconds / (double)_targetIntervalMs;
            return expected <= 0 ? 1.0 : Math.Min(1.0, attempts / expected);
        }
    }

    public event Action? StateChanged;
#pragma warning disable CS0067
    public event Action? LimitReached;
#pragma warning restore CS0067

    public void Start()
    {
        if (_running) return;
        _running = true;
        _paused = false;
        if (_mixer != null) { _mixer.Paused = false; _mixer.Enqueue = true; }
        _resumeSignal = new ManualResetEventSlim(true);
        _stopwatch.Restart();
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "ScreenForge video capture",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (!_running || _paused) return;
        _paused = true;
        if (_mixer != null) { _mixer.Paused = true; _mixer.Enqueue = false; }
        _resumeSignal?.Reset();
        _stopwatch.Stop();
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (!_paused) return;
        _stopwatch.Start();
        _paused = false;
        if (_mixer != null) { _mixer.Paused = false; _mixer.Enqueue = true; }
        _resumeSignal?.Set();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!_running && _thread == null) return;

        if (_paused)
        {
            _running = false;
        }
        else
        {
            _stopRequested = true;
            _graceUntilMs = _stopwatch.ElapsedMilliseconds + 1000;
        }

        _resumeSignal?.Set();
        var thread = _thread;
        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(6));
        _running = false;
        _thread = null;
        _resumeSignal?.Dispose();
        _resumeSignal = null;
        _stopwatch.Stop();
        FinishWriter();
        StateChanged?.Invoke();
    }

    public void Restart()
    {
        if (_disposed) return;

        _paused = false;
        if (_mixer != null)
        {
            _mixer.Paused = false;
            _mixer.Enqueue = false;
        }
        _running = false;
        _stopRequested = true;
        _graceUntilMs = 0;
        _resumeSignal?.Set();
        var thread = _thread;
        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(6));
        _thread = null;
        _resumeSignal?.Dispose();
        _resumeSignal = null;
        _stopwatch.Reset();

        OutputPath = null;
        Failure = null;
        TryDeleteTemp();
        _tempPath = Path.Combine(Path.GetTempPath(), $"screenforge-{Guid.NewGuid():N}.mp4");
        _finished = false;
        _frameCount = 0;
        _captureAttempts = 0;
        _audioBytes = 0;
        _stopRequested = false;

        Start();
    }

    private void OnMouse(MouseGesture gesture)
    {
        if (gesture.Type is not (MouseEventType.LeftDown or MouseEventType.RightDown or MouseEventType.MiddleDown))
            return;
        _clickX = gesture.X;
        _clickY = gesture.Y;
        _clickUntil = Environment.TickCount64 + 180;
        _clickArgb = gesture.Type == MouseEventType.RightDown
            ? unchecked((int)0x78FF0000)
            : gesture.Type == MouseEventType.MiddleDown
                ? unchecked((int)0x7800FFFF)
                : unchecked((int)0x78FFFF00);
    }

    private void CaptureLoop()
    {
        byte[]? pixels = null;
        byte[]? last = null;
        byte[]? scaled = null;
        long pauseOffset = 0;
        long pauseStarted = 0;

        try
        {
            _source = (IFrameSource?)DxgiFrameSource.TryCreate(_region) ?? new GdiFrameSource(_region);
            bool wantAudio = _mixer is { HasAudio: true };
            int bitrate = VideoBitrate.BitsPerSecond(_settings.Quality, _outWidth, _outHeight);
            _writer = new MfH264Writer(_tempPath, _outWidth, _outHeight, Fps, bitrate, wantAudio);
            pixels = new byte[_region.Width * _region.Height * 4];
            last = new byte[pixels.Length];
            if (_outWidth != _region.Width || _outHeight != _region.Height)
                scaled = new byte[_outWidth * _outHeight * 4];
            using var timerRes = new TimerResolution(1);

            long nextFrameAt = 0;
            while (_running)
            {
                if (_stopRequested && _stopwatch.ElapsedMilliseconds >= _graceUntilMs)
                    break;
                if (_paused)
                {
                    if (pauseStarted == 0)
                        pauseStarted = _stopwatch.ElapsedMilliseconds;
                    _resumeSignal?.Wait(100);
                    if (!_paused && pauseStarted != 0)
                    {
                        pauseOffset += _stopwatch.ElapsedMilliseconds - pauseStarted;
                        pauseStarted = 0;
                    }
                    nextFrameAt = _stopwatch.ElapsedMilliseconds;
                    continue;
                }

                FlushAudio(pauseOffset);

                long now = _stopwatch.ElapsedMilliseconds;
                long wait = nextFrameAt - now;
                if (wait > 1)
                {
                    Thread.Sleep((int)Math.Min(wait, 50));
                    continue;
                }

                bool got = _source.TryCopyBgra(pixels, out _);
                _captureAttempts++;
                if (got)
                {
                    OverlayPointer(pixels);
                    Buffer.BlockCopy(pixels, 0, last, 0, pixels.Length);
                }
                else if (_frameCount == 0)
                {
                    nextFrameAt += _targetIntervalMs;
                    continue;
                }
                else
                {
                    Buffer.BlockCopy(last, 0, pixels, 0, pixels.Length);
                }

                byte[] frame = pixels;
                if (scaled != null)
                {
                    ScaleNearest(pixels, _region.Width, _region.Height, scaled, _outWidth, _outHeight);
                    frame = scaled;
                }

                long timestamp = Math.Max(0, (_stopwatch.ElapsedMilliseconds - pauseOffset) * 10_000);
                _writer.WriteBgra(frame, timestamp);
                Interlocked.Increment(ref _frameCount);

                nextFrameAt += _targetIntervalMs;
                long drift = _stopwatch.ElapsedMilliseconds - nextFrameAt;
                if (drift > _targetIntervalMs * 3)
                    nextFrameAt = _stopwatch.ElapsedMilliseconds + _targetIntervalMs;
            }

            FlushAudio(pauseOffset);
        }
        catch (Exception ex)
        {
            Failure = ex.Message.Contains("H.264", StringComparison.OrdinalIgnoreCase)
                ? new InvalidOperationException("Bu cihazda H.264 kodlayıcı yok (Media Foundation).")
                : ex;
            _running = false;
        }
        finally
        {
            try { _mixer?.FlushRemainder(); } catch { /* ignore */ }
            try { FlushAudio(0); } catch { /* ignore */ }
            FinishWriter();
            _source?.Dispose();
            _source = null;
        }
    }

    private void FlushAudio(long pauseOffset)
    {
        if (_writer == null || _mixer == null) return;
        while (_mixer.TryDequeue(out var pcm))
        {
            long timestamp = _audioBytes * 10_000_000L / (AudioMixer.SampleRate * AudioMixer.BytesPerFrame);
            timestamp = Math.Max(0, timestamp - pauseOffset * 10_000);
            _writer.WriteAudio(pcm, timestamp);
            _audioBytes += pcm.Length;
        }
    }

    private void OverlayPointer(byte[] bgra)
    {
        if (!_settings.CaptureCursor && !_settings.HighlightClicks)
            return;

        int w = _region.Width, h = _region.Height;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { CopyPacked(bgra, data, w, h); }
        finally { bmp.UnlockBits(data); }

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (_settings.CaptureCursor)
                ScreenCapture.DrawCursor(g, _region);
            if (_settings.HighlightClicks && Environment.TickCount64 < _clickUntil)
            {
                int x = _clickX - _region.X;
                int y = _clickY - _region.Y;
                using var brush = new SolidBrush(System.Drawing.Color.FromArgb(_clickArgb));
                g.FillEllipse(brush, x - 14, y - 14, 28, 28);
            }
        }

        data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { CopyPacked(data, bgra, w, h); }
        finally { bmp.UnlockBits(data); }
    }

    private static void CopyPacked(byte[] packed, BitmapData data, int w, int h)
    {
        int srcStride = w * 4;
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(packed, y * srcStride, data.Scan0 + y * data.Stride, srcStride);
    }

    private static void CopyPacked(BitmapData data, byte[] packed, int w, int h)
    {
        int dstStride = w * 4;
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, packed, y * dstStride, dstStride);
    }

    private static void ScaleNearest(byte[] src, int sw, int sh, byte[] dst, int dw, int dh)
    {
        for (int y = 0; y < dh; y++)
        {
            int sy = y * sh / dh;
            int srcRow = sy * sw * 4;
            int dstRow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                int sx = x * sw / dw;
                int si = srcRow + sx * 4;
                int di = dstRow + x * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }
    }

    private void FinishWriter()
    {
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
            try { _writer?.Finish(); }
            catch (Exception ex) { Failure ??= ex; }
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;

            if (Failure == null && File.Exists(_tempPath) && _frameCount > 0)
                OutputPath = _tempPath;
            else
                TryDeleteTemp();
        }
    }

    private void TryDeleteTemp()
    {
        try
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }
        catch { /* temp leftover */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _hook?.Dispose();
        _mixer?.Dispose();
        if (OutputPath == null)
            TryDeleteTemp();
    }
}
