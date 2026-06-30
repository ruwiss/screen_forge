using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ScreenForge.Gif.Encoder;
using DrawingRect = System.Drawing.Rectangle;
using QType = ScreenForge.Gif.Encoder.QuantizerType;

namespace ScreenForge.Gif;

/// <summary>
/// Captures a screen region at a fixed FPS and encodes it to an animated GIF.
/// </summary>
public sealed class GifRecorder : IDisposable
{
    // ─── Win32 P/Invoke ───────────────────────────────────────────────────────
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hDst, int xDst, int yDst, int w, int h, IntPtr hSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    // ─── State ────────────────────────────────────────────────────────────────
    private readonly DrawingRect _pixelRegion;
    private readonly List<byte[]> _frames = new();
    private readonly List<int>    _frameDelays = new(); // ms per frame (for skipped frames)
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private byte[]? _lastFrame;
    private int _pendingDelayMs;

    public int Fps { get; }
    public int FrameCount => _frames.Count;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public int Width => _pixelRegion.Width;
    public int Height => _pixelRegion.Height;
    public List<byte[]> Frames => _frames;
    public List<int> FrameDelays => _frameDelays;
    public List<(int frameIndex, string keys)> KeyEvents { get; } = new();

    // Overlay gizleme hook'ları — GifRecordingOverlayWindow tarafından set edilir
    public Action? HideForCapture { get; set; }
    public Action? ShowAfterCapture { get; set; }

    public void RecordKey(string label) => KeyEvents.Add((_frames.Count, label));

    public GifRecorder(DrawingRect pixelRegion, int fps = 10)
    {
        _pixelRegion = pixelRegion;
        Fps = fps;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / fps),
        };
        _timer.Tick += (_, _) => CaptureFrame();
    }

    public void Start()
    {
        _lastFrame       = null;
        _pendingDelayMs  = 0;
        _stopwatch.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();
        // Son bekleyen delay varsa son frame'e yaz
        if (_frameDelays.Count > 0 && _pendingDelayMs > 0)
            _frameDelays[^1] += _pendingDelayMs;
        _pendingDelayMs = 0;
    }

    public async Task SaveAsync(string path, Action<double>? progress = null)
        => await SaveAsync(path, fpsOverride: null, colorCount: 256, framesOverride: null, progress: progress);

    public async Task SaveAsync(string path, int? fpsOverride, int colorCount, IList<byte[]>? framesOverride,
        int? widthOverride = null, int? heightOverride = null, Action<double>? progress = null,
        QType quantizerType = QType.Neural, int samplingFactor = 5,
        IList<int>? frameDelaysOverride = null, bool useGlobalPalette = false, bool dithering = false,
        bool optimizeUnchangedPixels = true)
    {
        var frames  = framesOverride   != null ? framesOverride.ToList()   : _frames.ToList();
        var delays  = frameDelaysOverride != null ? frameDelaysOverride.ToList() : _frameDelays.ToList();
        int w       = widthOverride  ?? _pixelRegion.Width;
        int h       = heightOverride ?? _pixelRegion.Height;
        int fps     = fpsOverride ?? Fps;
        int defaultDelayMs = (int)Math.Round(1000.0 / fps);

        await Task.Run(() =>
        {
            var framesToWrite = BuildFramesForExport(frames, delays, w, h, defaultDelayMs, optimizeUnchangedPixels);
            if (framesToWrite.Count == 0)
                return;

            using var fs  = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var gif = new GifFile(fs)
            {
                MaximumNumberColor = colorCount,
                RepeatCount        = 0,
                QuantizerType      = quantizerType,
                SamplingFactor     = samplingFactor,
                UseGlobalPalette   = useGlobalPalette,
                UseDithering       = dithering,
            };

            for (int i = 0; i < framesToWrite.Count; i++)
            {
                var frame = framesToWrite[i];
                gif.AddFrame(frame.Pixels, frame.Rect,
                    delayMs: frame.Delay, isLastFrame: i == framesToWrite.Count - 1);
                progress?.Invoke((double)(i + 1) / framesToWrite.Count);
            }
        });
    }

    private sealed class ExportFrame
    {
        public required byte[] Pixels { get; init; }
        public required Int32Rect Rect { get; init; }
        public int Delay { get; set; }
    }

    private static List<ExportFrame> BuildFramesForExport(
        List<byte[]> frames, List<int> delays, int width, int height, int defaultDelayMs, bool optimize)
    {
        var output = new List<ExportFrame>(frames.Count);
        if (frames.Count == 0)
            return output;

        byte[]? previous = null;
        for (int i = 0; i < frames.Count; i++)
        {
            int delay = delays.Count > i && delays[i] > 0 ? delays[i] : defaultDelayMs;
            var current = frames[i];

            if (!optimize || previous == null)
            {
                output.Add(new ExportFrame
                {
                    Pixels = current,
                    Rect = new Int32Rect(0, 0, width, height),
                    Delay = delay,
                });
                previous = current;
                continue;
            }

            var changed = FindChangedBounds(previous, current, width, height);
            if (changed.IsEmpty)
            {
                output[^1].Delay += delay;
                previous = current;
                continue;
            }

            output.Add(new ExportFrame
            {
                Pixels = CropFrame(current, width, changed),
                Rect = changed,
                Delay = delay,
            });
            previous = current;
        }

        return output;
    }

    private static Int32Rect FindChangedBounds(byte[] previous, byte[] current, int width, int height)
    {
        if (previous.Length != current.Length)
            return new Int32Rect(0, 0, width, height);

        var a = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(previous.AsSpan());
        var b = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(current.AsSpan());
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
                continue;

            int y = i / width;
            int x = i - y * width;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return maxX < minX || maxY < minY
            ? Int32Rect.Empty
            : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static byte[] CropFrame(byte[] source, int sourceWidth, Int32Rect rect)
    {
        var output = new byte[rect.Width * rect.Height * 4];
        int targetStride = rect.Width * 4;

        for (int y = 0; y < rect.Height; y++)
        {
            int sourceOffset = ((rect.Y + y) * sourceWidth + rect.X) * 4;
            Buffer.BlockCopy(source, sourceOffset, output, y * targetStride, targetStride);
        }

        return output;
    }

    // ─── Frame capture ────────────────────────────────────────────────────────

    private void CaptureFrame()
    {
        HideForCapture?.Invoke();

        IntPtr hScr = IntPtr.Zero;
        IntPtr hMem = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            int w = _pixelRegion.Width, h = _pixelRegion.Height;
            int x = _pixelRegion.X, y = _pixelRegion.Y;

            hScr = GetDC(IntPtr.Zero);
            hMem = CreateCompatibleDC(hScr);
            hBmp = CreateCompatibleBitmap(hScr, w, h);
            hOld = SelectObject(hMem, hBmp);

            if (!BitBlt(hMem, 0, 0, w, h, hScr, x, y, SRCCOPY))
                return;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;

            var buf = new byte[w * h * 4];
            if (GetDIBits(hMem, hBmp, 0, (uint)h, buf, ref bmi, 0) == 0)
                return;

            for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

            int frameDelayMs = (int)Math.Round(1000.0 / Fps);

            if (_lastFrame != null && AreIdentical(buf, _lastFrame))
            {
                _pendingDelayMs += frameDelayMs;
            }
            else
            {
                if (_frames.Count > 0 && _pendingDelayMs > 0)
                    _frameDelays[^1] += _pendingDelayMs;
                _pendingDelayMs = 0;
                _frames.Add(buf);
                _frameDelays.Add(frameDelayMs);
                _lastFrame = buf;
            }
        }
        finally
        {
            if (hMem != IntPtr.Zero && hOld != IntPtr.Zero)
                SelectObject(hMem, hOld);
            if (hBmp != IntPtr.Zero)
                DeleteObject(hBmp);
            if (hMem != IntPtr.Zero)
                DeleteDC(hMem);
            if (hScr != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hScr);

            ShowAfterCapture?.Invoke();
        }
    }

    private static bool AreIdentical(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        var ia = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(a.AsSpan());
        var ib = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(b.AsSpan());
        return ia.SequenceEqual(ib);
    }

    public void Dispose()
    {
        _timer.Stop();
        _frames.Clear();
        _frameDelays.Clear();
    }
}
