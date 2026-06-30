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
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();

    public int Fps { get; }
    public int FrameCount => _frames.Count;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public int Width => _pixelRegion.Width;
    public int Height => _pixelRegion.Height;
    public List<byte[]> Frames => _frames;
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
        _stopwatch.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();
    }

    public async Task SaveAsync(string path, Action<double>? progress = null)
        => await SaveAsync(path, fpsOverride: null, colorCount: 256, framesOverride: null, progress: progress);

    public async Task SaveAsync(string path, int? fpsOverride, int colorCount, IList<byte[]>? framesOverride,
        int? widthOverride = null, int? heightOverride = null, Action<double>? progress = null,
        QType quantizerType = QType.Neural, int samplingFactor = 5)
    {
        var frames  = framesOverride != null ? framesOverride.ToList() : _frames.ToList();
        int w       = widthOverride  ?? _pixelRegion.Width;
        int h       = heightOverride ?? _pixelRegion.Height;
        int fps     = fpsOverride ?? Fps;
        int delayMs = (int)Math.Round(1000.0 / fps);

        await Task.Run(() =>
        {
            using var fs  = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var gif = new GifFile(fs)
            {
                MaximumNumberColor = colorCount,
                RepeatCount        = 0,
                QuantizerType      = quantizerType,
                SamplingFactor     = samplingFactor,
            };

            for (int i = 0; i < frames.Count; i++)
            {
                gif.AddFrame(frames[i], new Int32Rect(0, 0, w, h),
                    delayMs: delayMs, isLastFrame: i == frames.Count - 1);
                progress?.Invoke((double)(i + 1) / frames.Count);
            }
        });
    }

    // ─── Frame capture ────────────────────────────────────────────────────────

    private void CaptureFrame()
    {
        // Overlay'i gizle — BitBlt'ye girmesin
        HideForCapture?.Invoke();

        int w = _pixelRegion.Width, h = _pixelRegion.Height;
        int x = _pixelRegion.X, y = _pixelRegion.Y;

        IntPtr hScr = GetDC(IntPtr.Zero);
        IntPtr hMem = CreateCompatibleDC(hScr);
        IntPtr hBmp = CreateCompatibleBitmap(hScr, w, h);
        IntPtr hOld = SelectObject(hMem, hBmp);
        BitBlt(hMem, 0, 0, w, h, hScr, x, y, SRCCOPY);
        SelectObject(hMem, hOld);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;

        var buf = new byte[w * h * 4];
        GetDIBits(hMem, hBmp, 0, (uint)h, buf, ref bmi, 0);

        DeleteObject(hBmp);
        DeleteDC(hMem);
        ReleaseDC(IntPtr.Zero, hScr);

        // GDI returns alpha=0 for screen pixels; fix so quantizer doesn't treat all as transparent
        for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

        _frames.Add(buf);

        // Overlay'i geri göster
        ShowAfterCapture?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _frames.Clear();
    }
}
