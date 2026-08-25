using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ScreenForge.Gif.Encoder;
using ScreenForge.Gif.Input;
using DrawingRect = System.Drawing.Rectangle;
using QType = ScreenForge.Gif.Encoder.QuantizerType;
using SfMouseButtons = ScreenForge.Gif.Input.MouseButtons;
using ScreenForge.Record;
using WpfKey = System.Windows.Input.Key;

namespace ScreenForge.Gif;

/// <summary>Kayıt oturumunun anlık durumu.</summary>
public enum GifRecorderState
{
    Idle,
    Recording,
    Paused,
    Stopped,
}

/// <summary>
/// Bir ekran bölgesini sabit FPS ile yakalar ve animasyonlu GIF'e kodlar.
/// Yakalama tamponları yeniden kullanılır; gecikmeler duvar saatinden ölçülür,
/// böylece zamanlayıcı sapması çıktı süresini bozmaz.
/// </summary>
public sealed class GifRecorder : IDisposable, IRecordingSession
{
    public const long DefaultMaxFrameBytes = 512L * 1024 * 1024;

    /// <summary>Tek karenin taşıyabileceği en uzun gecikme (GIF alanı 16 bit, 1/100 sn).</summary>
    private const int MaxDelayMs = 65535 * 10;

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
    private const uint CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    // ─── Durum ────────────────────────────────────────────────────────────────
    private readonly DrawingRect _pixelRegion;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private readonly long _maxFrameBytes;
    private readonly int _frameByteCount;
    private readonly int _targetIntervalMs;

    /// <summary>Kare listelerini yakalama ve UI iş parçacıkları arasında korur.</summary>
    private readonly object _frameLock = new();

    private Thread? _captureThread;
    private ManualResetEventSlim? _resumeSignal;
    private volatile bool _captureRunning;
    private volatile bool _capturePaused;

    private FrameStore _store;
    private List<int> _frameDelays = new();
    private List<FrameInput> _frameInputs = new();
    private byte[]? _lastFrame;        // karşılaştırma için son karenin ham hâli
    private byte[]? _scratch;          // yeniden kullanılan yakalama tamponu
    private long _frameBytes;

    /// <summary>Son <b>saklanan</b> karenin zaman damgası; gecikme buradan hesaplanır.</summary>
    private long _lastStoredTicks;

    private int _captureAttempts;
    private bool _disposed;

    // Yakalama boyunca canlı tutulan GDI kaynakları — kare başına alloc yok.
    private IntPtr _screenDc, _memoryDc, _bitmap, _oldBitmap;
    private BITMAPINFO _bitmapInfo;

    // ─── Girdi izleme ─────────────────────────────────────────────────────────
    private readonly InputHook _inputHook = new();
    private readonly CursorCapture _cursorCapture = new();
    private readonly HashSet<WpfKey> _pressedKeys = new();
    private readonly object _inputLock = new();

    private SfMouseButtons _buttons;
    private SfMouseButtons _clickedButtons; // son karedan beri basılan düğmeler
    private CursorSnapshot _cursor = CursorSnapshot.Hidden;

    public int Fps { get; }

    public int FrameCount
    {
        get { lock (_frameLock) return _store.Count; }
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>
    /// Hedeflenen kare sayısına göre gerçekte yakalanabilen oran (0-1).
    /// 1'in belirgin altındaysa bölge çok büyük ya da sistem yetişemiyordur.
    /// </summary>
    public double CaptureEfficiency
    {
        get
        {
            int attempts = _captureAttempts;
            if (attempts <= 0)
                return 1.0;

            double expected = _stopwatch.ElapsedMilliseconds / (double)_targetIntervalMs;
            return expected <= 0 ? 1.0 : Math.Min(1.0, attempts / expected);
        }
    }
    public int Width => _pixelRegion.Width;
    public int Height => _pixelRegion.Height;
    public long FrameBytes => Interlocked.Read(ref _frameBytes);
    public long MaxFrameBytes => _maxFrameBytes;
    public bool MemoryLimitReached { get; private set; }
    public GifRecorderState State { get; private set; } = GifRecorderState.Idle;
    public bool IsPaused => State == GifRecorderState.Paused;
    public List<int> FrameDelays => _frameDelays;
    public List<FrameInput> FrameInputs => _frameInputs;

    /// <summary>
    /// Kare saklamada elde edilen sıkıştırma oranı.
    /// Ekran görüntülerinde tipik olarak 20-30 kat.
    /// </summary>
    public double CompressionRatio
    {
        get { lock (_frameLock) return _store.Ratio; }
    }

    /// <summary>Kareler sıkıştırılmasaydı kaplayacakları bellek.</summary>
    public long UncompressedBytes
    {
        get { lock (_frameLock) return _store.RawBytes; }
    }

    /// <summary>İmleci yakalanan karelere çiz.</summary>
    public bool CaptureCursor { get; set; } = true;

    /// <summary>Fare tıklamalarını izle (tıklama vurgusu için gerekir).</summary>
    public bool TrackMouse { get; set; } = true;

    /// <summary>Klavye etkinliğini izle (tuş rozetleri için gerekir).</summary>
    public bool TrackKeyboard { get; set; } = true;

    /// <summary>Kullanılan bellek üst sınıra göre 0-1 arası oran.</summary>
    public double MemoryUsageRatio => _maxFrameBytes <= 0 ? 0 : Math.Min(1.0, (double)FrameBytes / _maxFrameBytes);

    // Overlay gizleme kancaları — WDA_EXCLUDEFROMCAPTURE yoksa yedek yol.
    public Action? HideForCapture { get; set; }
    public Action? ShowAfterCapture { get; set; }

    public event Action? LimitReached;
    public event Action? FrameMemoryLimitReached;
    public event Action? StateChanged;

    public GifRecorder(DrawingRect pixelRegion, int fps = 10, long maxFrameBytes = DefaultMaxFrameBytes)
    {
        if (maxFrameBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));

        _pixelRegion = pixelRegion;
        Fps = Math.Clamp(fps, 1, 60);
        _maxFrameBytes = maxFrameBytes;
        _frameByteCount = Math.Max(0, pixelRegion.Width) * Math.Max(0, pixelRegion.Height) * 4;
        _targetIntervalMs = Math.Max(1, (int)Math.Round(1000.0 / Fps));
        _store = new FrameStore(_frameByteCount);

        _inputHook.MouseActivity += OnMouseActivity;
        _inputHook.KeyActivity += OnKeyActivity;
    }

    // ─── Girdi kancaları ──────────────────────────────────────────────────────

    /// <summary>
    /// Kanca geri çağırmaları girdi hattında çalışır; burada yalnızca durum güncellenir.
    /// Kare üretimi yakalama zamanlayıcısında olur.
    /// </summary>
    private void OnMouseActivity(MouseGesture gesture)
    {
        lock (_inputLock)
        {
            _buttons = gesture.Buttons;

            // Hızlı tıklamada düğme iki kare arasında basılıp bırakılabilir.
            // Hangi düğme olduğunu sakla ki vurgu doğru renkle çizilsin.
            var pressed = gesture.Type switch
            {
                MouseEventType.LeftDown => SfMouseButtons.Left,
                MouseEventType.RightDown => SfMouseButtons.Right,
                MouseEventType.MiddleDown => SfMouseButtons.Middle,
                MouseEventType.ExtraDown => gesture.Buttons & (SfMouseButtons.Extra1 | SfMouseButtons.Extra2),
                _ => SfMouseButtons.None,
            };

            if (pressed != SfMouseButtons.None)
                _clickedButtons |= pressed;
        }
    }

    private void OnKeyActivity(KeyGesture gesture)
    {
        var label = KeyLabels.Describe(gesture.Key);
        if (label == null)
            return;

        lock (_inputLock)
        {
            if (gesture.IsDown) _pressedKeys.Add(gesture.Key);
            else _pressedKeys.Remove(gesture.Key);
        }
    }

    /// <summary>Bu kareye ait girdi anlık görüntüsünü alır ve biriken tıklamayı tüketir.</summary>
    private FrameInput CaptureInputSnapshot(CursorSnapshot cursor)
    {
        var input = new FrameInput
        {
            CursorX = cursor.X,
            CursorY = cursor.Y,
            CursorVisible = cursor.Visible,
        };

        lock (_inputLock)
        {
            // Hâlâ basılı olanlar + bu kare aralığında basılıp bırakılanlar.
            input.Buttons = _buttons | _clickedButtons;
            input.ClickStarted = input.Buttons != SfMouseButtons.None;
            _clickedButtons = SfMouseButtons.None;

            if (_pressedKeys.Count > 0)
            {
                foreach (var label in KeyLabels.Order(_pressedKeys))
                    input.Keys.Add(label);
            }
        }

        return input;
    }

    // ─── Oturum denetimi ──────────────────────────────────────────────────────

    public void Start()
    {
        if (State == GifRecorderState.Recording)
            return;

        _lastFrame = null;
        MemoryLimitReached = false;
        _captureAttempts = 0;
        _cursorCapture.Reset();

        // Fare ve klavye ayrı kancalardır; yalnızca gerekli olan kurulur.
        _inputHook.Start(mouse: TrackMouse, keyboard: TrackKeyboard);

        _stopwatch.Restart();
        _lastStoredTicks = 0;

        _capturePaused = false;
        _captureRunning = true;
        _resumeSignal = new ManualResetEventSlim(true);

        // Yakalama arayüz iş parçacığından ayrı çalışır. Aynı iş parçacığında
        // olsaydı düzen/çizim işleri kare aralığını kaçırmaya yol açardı.
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "ScreenForge GIF capture",
        };
        _captureThread.SetApartmentState(ApartmentState.STA);
        _captureThread.Start();

        SetState(GifRecorderState.Recording);
    }

    /// <summary>Kaydı duraklatır; toplanan kareler ve geçen süre korunur.</summary>
    public void Pause()
    {
        if (State != GifRecorderState.Recording)
            return;

        _capturePaused = true;
        _resumeSignal?.Reset();
        _stopwatch.Stop();
        SetState(GifRecorderState.Paused);
    }

    /// <summary>Duraklatılmış kaydı sürdürür. Duraklama süresi çıktıya gecikme olarak yansımaz.</summary>
    public void Resume()
    {
        if (State != GifRecorderState.Paused)
            return;

        _stopwatch.Start();
        _lastStoredTicks = _stopwatch.ElapsedMilliseconds;
        _capturePaused = false;
        _resumeSignal?.Set();
        SetState(GifRecorderState.Recording);
    }

    public void Stop()
    {
        _captureRunning = false;
        _resumeSignal?.Set();

        // Yakalama iş parçacığı GDI kaynaklarını kendi üzerinde tutar; kapanmasını
        // bekle ki temizlik doğru iş parçacığında yapılsın.
        var thread = _captureThread;
        if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));

        _captureThread = null;
        _resumeSignal?.Dispose();
        _resumeSignal = null;

        _stopwatch.Stop();
        _inputHook.Stop();
        ReleaseCaptureResources();

        lock (_inputLock)
        {
            _pressedKeys.Clear();
            _buttons = SfMouseButtons.None;
            _clickedButtons = SfMouseButtons.None;
        }

        if (State is GifRecorderState.Stopped or GifRecorderState.Idle)
            return;

        SetState(GifRecorderState.Stopped);
    }

    public void Restart()
    {
        if (_disposed) return;
        if (State is GifRecorderState.Idle or GifRecorderState.Stopped)
        {
            Start();
            return;
        }

        lock (_frameLock)
        {
            _store.Clear();
            _frameDelays.Clear();
            _frameInputs.Clear();
            _lastFrame = null;
            _frameBytes = 0;
        }
        MemoryLimitReached = false;
        _captureAttempts = 0;
        _lastStoredTicks = 0;
        _stopwatch.Restart();
        lock (_inputLock)
        {
            _pressedKeys.Clear();
            _buttons = SfMouseButtons.None;
            _clickedButtons = SfMouseButtons.None;
        }
        if (State == GifRecorderState.Paused)
            Resume();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Sabit aralıklarla kare yakalar. Bir yakalama uzun sürerse sonraki
    /// bekleme kısaltılır, böylece uzun vadede hız hedefe yakın kalır.
    /// </summary>
    private void CaptureLoop()
    {
        AcquireCaptureResources();

        // Windows zamanlayıcı çözünürlüğü varsayılan 15.6 ms'dir; 30+ fps için
        // bu tek başına kare kaçırmaya yeter.
        using var _ = new TimerResolution(1);

        long nextFrameAt = 0;

        while (_captureRunning)
        {
            if (_capturePaused)
            {
                _resumeSignal?.Wait(100);
                nextFrameAt = _stopwatch.ElapsedMilliseconds;
                continue;
            }

            long now = _stopwatch.ElapsedMilliseconds;
            long wait = nextFrameAt - now;

            if (wait > 1)
            {
                Thread.Sleep((int)Math.Min(wait, 50));
                continue;
            }

            CaptureFrame();
            _captureAttempts++;

            // Bir sonraki hedefi sabit ızgara üzerinde ilerlet. Çok geri
            // kaldıysak ızgarayı şimdiye çek; yoksa açık kaçırılan kareleri
            // kapatmak için sonsuza dek koşarız.
            nextFrameAt += _targetIntervalMs;

            long drift = _stopwatch.ElapsedMilliseconds - nextFrameAt;
            if (drift > _targetIntervalMs * 3)
                nextFrameAt = _stopwatch.ElapsedMilliseconds + _targetIntervalMs;
        }

        ReleaseCaptureResources();
    }

    private void SetState(GifRecorderState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke();
    }

    // ─── Kare yakalama ────────────────────────────────────────────────────────

    private void AcquireCaptureResources()
    {
        if (_memoryDc != IntPtr.Zero)
            return;

        int w = _pixelRegion.Width, h = _pixelRegion.Height;
        if (w <= 0 || h <= 0)
            return;

        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        _bitmap = CreateCompatibleBitmap(_screenDc, w, h);
        _oldBitmap = SelectObject(_memoryDc, _bitmap);

        _bitmapInfo = new BITMAPINFO();
        _bitmapInfo.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        _bitmapInfo.bmiHeader.biWidth = w;
        _bitmapInfo.bmiHeader.biHeight = -h; // yukarıdan aşağı
        _bitmapInfo.bmiHeader.biPlanes = 1;
        _bitmapInfo.bmiHeader.biBitCount = 32;

        _scratch ??= new byte[_frameByteCount];
    }

    private void ReleaseCaptureResources()
    {
        if (_memoryDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_memoryDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        if (_screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screenDc);

        _screenDc = _memoryDc = _bitmap = _oldBitmap = IntPtr.Zero;
    }

    private void CaptureFrame()
    {
        if (_memoryDc == IntPtr.Zero || _scratch == null)
            return;

        HideForCapture?.Invoke();

        try
        {
            int w = _pixelRegion.Width, h = _pixelRegion.Height;
            if (!BitBlt(_memoryDc, 0, 0, w, h, _screenDc, _pixelRegion.X, _pixelRegion.Y, SRCCOPY | CAPTUREBLT))
                return;

            // İmleç ekran yakalamasına dahil değildir; ayrıca çizilir.
            _cursor = CaptureCursor
                ? _cursorCapture.DrawInto(_memoryDc, _pixelRegion.X, _pixelRegion.Y)
                : CursorSnapshot.Hidden;

            if (GetDIBits(_memoryDc, _bitmap, 0, (uint)h, _scratch, ref _bitmapInfo, 0) == 0)
                return;

            long now = _stopwatch.ElapsedMilliseconds;
            var input = CaptureInputSnapshot(ClampCursor(_cursor, w, h));

            // Aynı kare: yeni tampon ayırma. Süre son saklanan karenin üzerine
            // yazılır; böylece durağan bölümler gerçek süresini korur.
            // Girdi etkinliği varsa kare yine kaydedilir, yoksa tıklama vurgusu
            // ve tuş rozetleri kaybolur.
            if (_lastFrame != null && !input.HasAnyInput && AreIdentical(_scratch, _lastFrame))
            {
                lock (_frameLock)
                {
                    if (_frameDelays.Count > 0)
                        _frameDelays[^1] = ClampDelay(now - _lastStoredTicks);
                }
                return;
            }

            var frame = new byte[_frameByteCount];
            Buffer.BlockCopy(_scratch, 0, frame, 0, _frameByteCount);
            SetOpaque(frame);

            bool stored;
            lock (_frameLock)
            {
                // Önceki karenin süresi, o kare ekrandayken geçen gerçek zamandır.
                if (_store.Count > 0 && _frameDelays.Count > 0)
                    _frameDelays[^1] = ClampDelay(now - _lastStoredTicks);

                stored = TryStoreFrameCore(frame, _targetIntervalMs, input);
                if (stored)
                    _lastStoredTicks = now;
            }

            if (!stored)
            {
                Stop();
                LimitReached?.Invoke();
                FrameMemoryLimitReached?.Invoke();
            }
        }
        finally
        {
            ShowAfterCapture?.Invoke();
        }
    }

    /// <summary>
    /// Alfa kanalını tümüyle opak yapar.
    /// 32 bitlik sözcükler hâlinde işlenir; bayt bayt döngü büyük karelerde
    /// kare bütçesinin belirgin bir kısmını yiyordu.
    /// </summary>
    private static void SetOpaque(byte[] bgra)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] |= 0xFF000000u;
    }

    private static int ClampDelay(long milliseconds)
        => (int)Math.Clamp(milliseconds, 1, MaxDelayMs);

    private static bool AreIdentical(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    /// <summary>İmleci kare sınırlarına kırpar; dışarıdaysa görünmez sayılır.</summary>
    private static CursorSnapshot ClampCursor(CursorSnapshot cursor, int width, int height)
    {
        if (!cursor.Visible || cursor.X < 0 || cursor.Y < 0 || cursor.X >= width || cursor.Y >= height)
            return CursorSnapshot.Hidden;

        return cursor;
    }

    internal bool TryStoreFrame(byte[] frame, int delayMs, FrameInput? input = null)
    {
        lock (_frameLock)
            return TryStoreFrameCore(frame, delayMs, input);
    }

    /// <summary>Saklanan kareleri açar. Depo kendisi korunur.</summary>
    internal List<byte[]> DrainFramesForExport()
    {
        lock (_frameLock)
        {
            var frames = new List<byte[]>(_store.Count);
            for (int i = 0; i < _store.Count; i++)
                frames.Add(_store.Get(i));

            return frames;
        }
    }

    /// <summary>Çağıran <see cref="_frameLock"/> kilidini tutuyor olmalıdır.</summary>
    private bool TryStoreFrameCore(byte[] frame, int delayMs, FrameInput? input)
    {
        // Sınır sıkıştırılmış boyuta uygulanır; kare önce sıkıştırılır.
        long stored = _store.Add(frame);

        if (_frameBytes + stored > _maxFrameBytes)
        {
            _store.RemoveLast(stored);
            MemoryLimitReached = true;
            return false;
        }

        _frameDelays.Add(Math.Clamp(delayMs, 1, MaxDelayMs));
        _frameInputs.Add(input ?? new FrameInput());
        _lastFrame = frame;
        _frameBytes += stored;
        return true;
    }

    internal (List<byte[]> Frames, List<int> FrameDelays, List<FrameInput> Inputs) DetachFrames()
    {
        Stop();

        lock (_frameLock)
        {
            // Kareler burada açılır; düzenleyici ham piksellerle çalışır.
            var frames = _store.DrainAll();
            var frameDelays = _frameDelays;
            var inputs = _frameInputs;

            // Girdi listesi kare listesiyle aynı uzunlukta olmalı.
            while (inputs.Count < frames.Count) inputs.Add(new FrameInput());

            _store = new FrameStore(_frameByteCount);
            _frameDelays = new List<int>();
            _frameInputs = new List<FrameInput>();
            _lastFrame = null;
            _frameBytes = 0;
            return (frames, frameDelays, inputs);
        }
    }

    // ─── Dışa aktarma ─────────────────────────────────────────────────────────

    public async Task SaveAsync(string path, Action<double>? progress = null)
        => await SaveAsync(path, new GifExportOptions(), progress: progress);

    /// <summary>
    /// Kareleri GIF olarak yazar. Kaynak kareler <paramref name="options"/> içindeki
    /// <see cref="GifExportOptions.Frames"/> ile geçersiz kılınabilir.
    /// </summary>
    public async Task SaveAsync(string path, GifExportOptions options,
        Action<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var frames = options.Frames?.ToList() ?? DrainFramesForExport();
        var delays = options.FrameDelays?.ToList() ?? _frameDelays.ToList();
        var inputs = options.FrameInputs?.ToList() ?? _frameInputs.ToList();
        int width = options.Width ?? _pixelRegion.Width;
        int height = options.Height ?? _pixelRegion.Height;
        int fps = options.Fps ?? Fps;
        int defaultDelayMs = (int)Math.Round(1000.0 / Math.Max(1, fps));

        await Task.Run(() =>
        {
            // Kaplamalar delta hesabından ÖNCE uygulanmalı; aksi hâlde çizilen
            // pikseller "değişmedi" sayılıp saydam yazılır.
            frames = ApplyInputOverlay(frames, inputs, width, height, options, cancellationToken);
            frames = ApplyOverlaySet(frames, delays, width, height, defaultDelayMs, options, cancellationToken);

            var plan = BuildFramesForExport(frames, delays, width, height, defaultDelayMs,
                options.OptimizeUnchangedPixels, options.ChangeTolerance);

            if (plan.Count == 0)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            // Geçici dosyaya yaz, sonra taşı: yarım kalan çıktı hedefi bozmasın.
            string tempPath = path + ".tmp";

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var gif = new GifFile(stream)
                {
                    MaximumNumberColor = Math.Clamp(options.ColorCount, 2, 256),
                    RepeatCount = options.RepeatCount,
                    QuantizerType = options.QuantizerType,
                    SamplingFactor = options.SamplingFactor,
                    UseGlobalPalette = options.UseGlobalPalette,
                    UseDithering = options.Dithering,
                })
                {
                    gif.SetCanvasSize(width, height);

                    if (options.UseGlobalPalette)
                        gif.BuildGlobalPalette(SelectPaletteSamples(frames));

                    for (int i = 0; i < plan.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var frame = plan[i];
                        gif.AddFrame(frame.Pixels, frame.Rect, frame.Delay, frame.HasTransparency);
                        progress?.Invoke((double)(i + 1) / plan.Count);
                    }
                }

                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* geçici dosya temizliği kritik değil */ }
    }

    /// <summary>
    /// Tıklama vurgusu ve tuş rozetlerini karelere işler.
    /// Kaplama gerekmeyen kareler kopyalanmaz, özgün dizi paylaşılır.
    /// </summary>
    private List<byte[]> ApplyInputOverlay(List<byte[]> frames, List<FrameInput> inputs,
        int width, int height, GifExportOptions options, CancellationToken cancellationToken)
    {
        var overlay = options.InputOverlay;
        if (overlay == null || !overlay.HasWork || inputs.Count == 0)
            return frames;

        // Kaynak boyuttan çıktı boyutuna ölçek — yeniden boyutlandırılmış karelerde
        // vurgu yarıçapı ve rozet yazısı orantılı kalsın.
        double scale = _pixelRegion.Width > 0 ? (double)width / _pixelRegion.Width : 1.0;

        var result = new List<byte[]>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var input = i < inputs.Count ? inputs[i] : null;
            result.Add(InputOverlayRenderer.Apply(frames[i], width, height, input, overlay, scale));
        }

        return result;
    }

    /// <summary>Altyazı, ilerleme göstergesi, kenarlık ve filigranı karelere çizer.</summary>
    private List<byte[]> ApplyOverlaySet(List<byte[]> frames, List<int> delays,
        int width, int height, int defaultDelayMs, GifExportOptions options, CancellationToken cancellationToken)
    {
        var set = options.Overlays;
        if (set == null || !set.HasWork || frames.Count == 0)
            return frames;

        double scale = _pixelRegion.Width > 0 ? (double)width / _pixelRegion.Width : 1.0;

        long total = 0;
        for (int i = 0; i < frames.Count; i++)
            total += delays.Count > i && delays[i] > 0 ? delays[i] : defaultDelayMs;

        var result = new List<byte[]>(frames.Count);
        long elapsed = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            elapsed += delays.Count > i && delays[i] > 0 ? delays[i] : defaultDelayMs;
            result.Add(Editing.OverlayRenderer.Apply(frames[i], width, height, set,
                i, frames.Count, elapsed, total, scale));
        }

        return result;
    }

    /// <summary>Global palet için kareleri seyreltir — en fazla 24 kare yeter.</summary>
    private static List<byte[]> SelectPaletteSamples(List<byte[]> frames)
    {
        const int MaxSamples = 24;
        if (frames.Count <= MaxSamples)
            return frames;

        var samples = new List<byte[]>(MaxSamples);
        double step = (double)frames.Count / MaxSamples;
        for (int i = 0; i < MaxSamples; i++)
            samples.Add(frames[(int)(i * step)]);

        return samples;
    }

    internal sealed class ExportFrame
    {
        public required byte[] Pixels { get; init; }
        public required Int32Rect Rect { get; init; }
        public required bool HasTransparency { get; init; }
        public int Delay { get; set; }
    }

    /// <summary>
    /// Kareleri yazıma hazırlar: değişmeyen bölge kırpılır ve değişmeyen pikseller
    /// saydam işaretlenir; böylece LZW uzun tekrar dizileri üretir ve dosya küçülür.
    /// </summary>
    internal static List<ExportFrame> BuildFramesForExport(
        List<byte[]> frames, List<int> delays, int width, int height,
        int defaultDelayMs, bool optimize, int tolerance = 0)
    {
        var output = new List<ExportFrame>(frames.Count);
        if (frames.Count == 0 || width <= 0 || height <= 0)
            return output;

        byte[]? previous = null;

        for (int i = 0; i < frames.Count; i++)
        {
            int delay = Math.Clamp(delays.Count > i && delays[i] > 0 ? delays[i] : defaultDelayMs, 1, MaxDelayMs);
            var current = frames[i];

            if (!optimize || previous == null || previous.Length != current.Length)
            {
                output.Add(new ExportFrame
                {
                    Pixels = current,
                    Rect = new Int32Rect(0, 0, width, height),
                    HasTransparency = false,
                    Delay = delay,
                });
                previous = current;
                continue;
            }

            var changed = FindChangedBounds(previous, current, width, height, tolerance);
            if (changed.IsEmpty)
            {
                // Kare öncekiyle aynı: yeni blok yerine süresini uzat.
                output[^1].Delay = Math.Min(MaxDelayMs, output[^1].Delay + delay);
                previous = current;
                continue;
            }

            var (pixels, hasTransparency) = CropAndMask(previous, current, width, changed, tolerance);

            output.Add(new ExportFrame
            {
                Pixels = pixels,
                Rect = changed,
                HasTransparency = hasTransparency,
                Delay = delay,
            });
            previous = current;
        }

        return output;
    }

    /// <summary>Değişen piksellerin sınırlayıcı dikdörtgeni; değişiklik yoksa boş.</summary>
    internal static Int32Rect FindChangedBounds(byte[] previous, byte[] current, int width, int height, int tolerance = 0)
    {
        if (previous.Length != current.Length)
            return new Int32Rect(0, 0, width, height);

        var a = MemoryMarshal.Cast<byte, int>(previous.AsSpan());
        var b = MemoryMarshal.Cast<byte, int>(current.AsSpan());
        int count = Math.Min(a.Length, width * height);

        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            if (rowStart >= count)
                break;

            int rowEnd = Math.Min(rowStart + width, count);
            int rowMinX = -1, rowMaxX = -1;

            // Satır başından ilk farkı ara.
            for (int i = rowStart; i < rowEnd; i++)
            {
                if (IsSame(a[i], b[i], tolerance))
                    continue;
                rowMinX = i - rowStart;
                break;
            }

            if (rowMinX < 0)
                continue;

            // Satır sonundan son farkı ara.
            for (int i = rowEnd - 1; i >= rowStart; i--)
            {
                if (IsSame(a[i], b[i], tolerance))
                    continue;
                rowMaxX = i - rowStart;
                break;
            }

            if (rowMinX < minX) minX = rowMinX;
            if (rowMaxX > maxX) maxX = rowMaxX;
            if (y < minY) minY = y;
            maxY = y;
        }

        return maxX < minX || maxY < minY
            ? Int32Rect.Empty
            : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>İki BGRA pikselinin tolerans dahilinde aynı sayılıp sayılmadığı.</summary>
    private static bool IsSame(int a, int b, int tolerance)
    {
        if (a == b)
            return true;
        if (tolerance <= 0)
            return false;

        return Math.Abs(((a >> 16) & 0xff) - ((b >> 16) & 0xff)) <= tolerance
            && Math.Abs(((a >> 8) & 0xff) - ((b >> 8) & 0xff)) <= tolerance
            && Math.Abs((a & 0xff) - (b & 0xff)) <= tolerance;
    }

    /// <summary>
    /// Kareyi <paramref name="rect"/> alanına kırpar ve önceki kareyle aynı kalan
    /// pikselleri alfa 0 yaparak saydam işaretler.
    /// </summary>
    internal static (byte[] Pixels, bool HasTransparency) CropAndMask(
        byte[] previous, byte[] current, int sourceWidth, Int32Rect rect, int tolerance = 0)
    {
        var output = new byte[rect.Width * rect.Height * 4];
        var outputPixels = MemoryMarshal.Cast<byte, int>(output.AsSpan());
        var previousPixels = MemoryMarshal.Cast<byte, int>(previous.AsSpan());
        var currentPixels = MemoryMarshal.Cast<byte, int>(current.AsSpan());

        bool hasTransparency = false;

        for (int y = 0; y < rect.Height; y++)
        {
            int sourceRow = (rect.Y + y) * sourceWidth + rect.X;
            int targetRow = y * rect.Width;

            for (int x = 0; x < rect.Width; x++)
            {
                int source = sourceRow + x;
                int value = currentPixels[source];

                if (IsSame(previousPixels[source], value, tolerance))
                {
                    outputPixels[targetRow + x] = 0; // alfa 0 → "değişmedi"
                    hasTransparency = true;
                    continue;
                }

                outputPixels[targetRow + x] = value | unchecked((int)0xff000000);
            }
        }

        return (output, hasTransparency);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Yakalama iş parçacığını durdurur ve GDI kaynaklarını serbest bırakır.
        Stop();

        _inputHook.MouseActivity -= OnMouseActivity;
        _inputHook.KeyActivity -= OnKeyActivity;
        _inputHook.Dispose();

        lock (_frameLock)
        {
            _store.Clear();
            _frameDelays.Clear();
            _frameInputs.Clear();
            _lastFrame = null;
            _frameBytes = 0;
        }

        lock (_inputLock)
            _pressedKeys.Clear();

        _scratch = null;
    }
}

/// <summary>GIF dışa aktarma ayarları.</summary>
public sealed class GifExportOptions
{
    /// <summary>Kayıttaki yerine kullanılacak kareler.</summary>
    public IList<byte[]>? Frames { get; init; }

    /// <summary>Kare başına gecikme (ms). Verilmezse FPS'ten türetilir.</summary>
    public IList<int>? FrameDelays { get; init; }

    /// <summary>Kare başına girdi bilgisi (imleç, tıklama, tuşlar).</summary>
    public IList<FrameInput>? FrameInputs { get; init; }

    /// <summary>
    /// Tıklama vurgusu ve tuş rozeti çizim ayarları.
    /// <see langword="null"/> ise kaplama uygulanmaz.
    /// </summary>
    public InputOverlayOptions? InputOverlay { get; init; }

    /// <summary>
    /// Altyazı, ilerleme göstergesi, kenarlık ve filigran ayarları.
    /// <see langword="null"/> ise kaplama uygulanmaz.
    /// </summary>
    public Editing.OverlaySet? Overlays { get; init; }

    public int? Fps { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }

    /// <summary>Palet boyutu (2-256).</summary>
    public int ColorCount { get; init; } = 256;

    public QType QuantizerType { get; init; } = QType.Neural;

    /// <summary>Neural örnekleme faktörü (1-20).</summary>
    public int SamplingFactor { get; init; } = 5;

    public bool UseGlobalPalette { get; init; }
    public bool Dithering { get; init; }

    /// <summary>0 = sonsuz döngü, -1 = döngü yok.</summary>
    public int RepeatCount { get; init; }

    /// <summary>Değişmeyen bölgeleri kırp ve saydam yap.</summary>
    public bool OptimizeUnchangedPixels { get; init; } = true;

    /// <summary>
    /// "Değişmedi" sayılması için kanal başına izin verilen fark (0-32).
    /// Yükseltmek dosyayı küçültür, hafif hayalet iz bırakabilir.
    /// </summary>
    public int ChangeTolerance { get; init; }
}
