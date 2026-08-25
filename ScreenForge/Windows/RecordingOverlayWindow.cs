using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ScreenForge.Gif;
using ScreenForge.Record;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// GIF veya video kaydı sırasında gösterilen overlay.
/// İki pencere:
///   (1) Tam ekran şeffaf kesikli çerçeve — WS_EX_TRANSPARENT + WDA_EXCLUDEFROMCAPTURE
///   (2) Küçük opak kontrol çubuğu (AllowsTransparency=false) — tıklanabilir
/// </summary>
public sealed class RecordingOverlayWindow
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    private static readonly Color AccentColor = Color.FromRgb(0xEA, 0x6F, 0x12);
    private static readonly Color RecordColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color MutedColor = Color.FromRgb(0x9A, 0xA4, 0xB8);
    private static readonly Color WarningColor = Color.FromRgb(0xF2, 0xB0, 0x24);

    public event Action<IRecordingSession>? Stopped;

    private readonly IRecordingSession _session;
    private readonly Rect _dipRegion;
    private readonly RecordingKind _kind;

    public RecordingOverlayWindow(IRecordingSession session, Rect dipRegion, RecordingKind kind)
    {
        _session = session;
        _dipRegion = dipRegion;
        _kind = kind;
    }

    public void Show()
    {
        IntPtr keyboardHook = IntPtr.Zero;
        LowLevelKeyboardProc? hookProc = null;

        var dashRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 6, 3 },
            StrokeDashCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Width = _dipRegion.Width,
            Height = _dipRegion.Height,
        };
        var borderCanvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        borderCanvas.Children.Add(dashRect);
        Canvas.SetLeft(dashRect, _dipRegion.Left);
        Canvas.SetTop(dashRect, _dipRegion.Top);

        var borderWin = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            IsHitTestVisible = false,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Content = borderCanvas,
        };
        borderWin.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(borderWin).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };

        var recDot = new TextBlock
        {
            Text = "●",
            Foreground = new SolidColorBrush(RecordColor),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var elapsedText = new TextBlock
        {
            Text = "00:00",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var frameText = new TextBlock
        {
            Text = _kind == RecordingKind.Video ? "MP4" : "0 kare",
            Foreground = new SolidColorBrush(MutedColor),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var rateText = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(MutedColor),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Hedef hıza ulaşma oranı. Düşükse alanı küçültün veya FPS'i azaltın.",
        };
        var memoryText = new TextBlock
        {
            Text = "0 MB",
            Foreground = new SolidColorBrush(MutedColor),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Karelerin kullandığı bellek",
            Visibility = _kind == RecordingKind.Gif ? Visibility.Visible : Visibility.Collapsed,
        };
        var memoryBar = new WpfProgressBar
        {
            Width = 46,
            Height = 4,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = new SolidColorBrush(AccentColor),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x45)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Visibility = _kind == RecordingKind.Gif ? Visibility.Visible : Visibility.Collapsed,
        };
        var sysMeter = new WpfProgressBar
        {
            Width = 42, Height = 5, Minimum = 0, Maximum = 100,
            Foreground = new SolidColorBrush(AccentColor),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x45)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Sistem sesi",
            Visibility = Visibility.Collapsed,
        };
        var micMeter = new WpfProgressBar
        {
            Width = 42, Height = 5, Minimum = 0, Maximum = 100,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x45)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Mikrofon",
            Visibility = Visibility.Collapsed,
        };

        var pauseBtn = MakeBarButton("Duraklat", Color.FromRgb(0x2A, 0x35, 0x4D), 78);
        pauseBtn.ToolTip = "Duraklat / Devam et  (Ctrl+Shift+P)";
        var stopBtn = MakeBarButton("Durdur", Color.FromRgb(0xBE, 0x3A, 0x3A), 72);
        stopBtn.ToolTip = "Kaydı bitir  (Esc)";
        var restartBtn = MakeBarButton("Yeniden", Color.FromRgb(0x2A, 0x35, 0x4D), 72);
        restartBtn.ToolTip = "Kaydı sil ve baştan başla";

        var barStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 5, 10, 5),
        };
        barStack.Children.Add(recDot);
        barStack.Children.Add(elapsedText);
        barStack.Children.Add(frameText);
        barStack.Children.Add(rateText);
        barStack.Children.Add(memoryText);
        barStack.Children.Add(memoryBar);
        barStack.Children.Add(sysMeter);
        barStack.Children.Add(micMeter);
        barStack.Children.Add(pauseBtn);
        barStack.Children.Add(restartBtn);
        barStack.Children.Add(stopBtn);

        double barLeft = SystemParameters.VirtualScreenLeft + _dipRegion.Left;
        double barTop = SystemParameters.VirtualScreenTop + Math.Max(4, _dipRegion.Top - 44);

        var barWin = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x32)),
            Topmost = true,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Left = barLeft,
            Top = barTop,
            Content = barStack,
        };
        barWin.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(barWin).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            WindowChrome.SetWindowChrome(barWin, new WindowChrome
            {
                GlassFrameThickness = new Thickness(0),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        };

        if (_session is GifRecorder gifSession)
        {
            gifSession.HideForCapture = null;
            gifSession.ShowAfterCapture = null;
        }

        bool stopping = false;

        void UpdateRecordingVisuals()
        {
            bool paused = _session.IsPaused;
            pauseBtn.Content = paused ? "Devam" : "Duraklat";
            recDot.Foreground = new SolidColorBrush(paused ? MutedColor : RecordColor);
            recDot.Opacity = paused ? 0.45 : 1.0;
            dashRect.Stroke = new SolidColorBrush(paused
                ? Color.FromArgb(150, 154, 164, 184)
                : Color.FromArgb(220, 255, 255, 255));
        }

        void TogglePause()
        {
            if (stopping) return;
            if (_session.IsPaused) _session.Resume();
            else _session.Pause();
            UpdateRecordingVisuals();
        }

        void DoStop()
        {
            if (stopping) return;
            stopping = true;
            if (_kind == RecordingKind.Video)
            {
                stopBtn.Content = "Duruyor";
                stopBtn.IsEnabled = false;
                pauseBtn.IsEnabled = false;
                restartBtn.IsEnabled = false;
                barWin.UpdateLayout();
            }
            _session.LimitReached -= OnLimitReached;
            _session.Stop();
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            borderWin.Close();
            barWin.Close();
            Stopped?.Invoke(_session);
        }

        void OnLimitReached()
        {
            if (_kind != RecordingKind.Gif || _session is not GifRecorder gif)
                return;

            barWin.Dispatcher.BeginInvoke(() =>
            {
                if (stopping) return;
                long limitMb = gif.MaxFrameBytes / (1024 * 1024);
                var elapsed = gif.Elapsed;
                MessageBox.Show(barWin,
                    $"Kayıt {limitMb} MB bellek sınırına ulaştı ve durduruldu.\n\n" +
                    $"{gif.FrameCount} kare · {elapsed.TotalSeconds:0} saniye yakalandı ve düzenleyicide açılacak.\n\n" +
                    "Daha uzun kayıt için: daha küçük bir alan seçin, FPS'i düşürün " +
                    "veya Ayarlar'dan bellek sınırını artırın.",
                    "ScreenForge", MessageBoxButton.OK, MessageBoxImage.Information);
                DoStop();
            });
        }

        _session.LimitReached += OnLimitReached;
        pauseBtn.Click += (_, _) => TogglePause();
        stopBtn.Click += (_, _) => DoStop();
        restartBtn.Click += (_, _) =>
        {
            if (stopping) return;
            restartBtn.IsEnabled = false;
            try { _session.Restart(); }
            finally { if (!stopping) restartBtn.IsEnabled = true; }
            UpdateRecordingVisuals();
        };
        barWin.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DoStop();
            else if (e.Key == Key.Space) TogglePause();
        };
        barWin.Closing += (_, e) =>
        {
            if (stopping) return;
            e.Cancel = true;
            barWin.Dispatcher.BeginInvoke(DoStop);
        };
        borderWin.Closing += (_, e) =>
        {
            if (stopping) return;
            e.Cancel = true;
            barWin.Dispatcher.BeginInvoke(DoStop);
        };

        var uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_kind == RecordingKind.Video ? 50 : 500) };
        uiTimer.Tick += (_, _) =>
        {
            var elapsed = _session.Elapsed;
            elapsedText.Text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            if (_kind == RecordingKind.Gif && _session is GifRecorder gif)
            {
                frameText.Text = $"{gif.FrameCount} kare";
                double ratio = gif.MemoryUsageRatio;
                memoryText.Text = $"{gif.FrameBytes / (1024.0 * 1024.0):0.#} MB";
                memoryText.ToolTip = $"Sıkıştırılmış {gif.FrameBytes / (1024.0 * 1024.0):0.#} MB " +
                                     $"/ sınır {gif.MaxFrameBytes / (1024 * 1024)} MB\n" +
                                     $"Ham karşılığı {gif.UncompressedBytes / (1024.0 * 1024.0):0} MB " +
                                     $"({gif.CompressionRatio:0.#}× sıkıştırma)";
                memoryBar.Value = ratio * 100;
                var gaugeColor = ratio >= 0.85 ? RecordColor : ratio >= 0.6 ? WarningColor : AccentColor;
                memoryBar.Foreground = new SolidColorBrush(gaugeColor);
                memoryText.Foreground = new SolidColorBrush(ratio >= 0.85 ? RecordColor : MutedColor);
            }
            if (_session is VideoRecorder video)
            {
                sysMeter.Visibility = video.HasSystemAudio ? Visibility.Visible : Visibility.Collapsed;
                micMeter.Visibility = video.HasMic ? Visibility.Visible : Visibility.Collapsed;
                sysMeter.Value = video.SystemPeak * 100;
                micMeter.Value = video.MicPeak * 100;
            }

            double efficiency = _session.CaptureEfficiency;
            if (elapsed.TotalMilliseconds > 800 && efficiency < 0.9)
            {
                rateText.Text = $"{_session.Fps * efficiency:0} fps";
                rateText.Foreground = new SolidColorBrush(efficiency < 0.7 ? RecordColor : WarningColor);
            }
            else
            {
                rateText.Text = "";
            }
        };

        var blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool blinkOn = true;
        blinkTimer.Tick += (_, _) =>
        {
            if (_session.IsPaused) return;
            blinkOn = !blinkOn;
            recDot.Opacity = blinkOn ? 1.0 : 0.2;
        };

        barWin.Loaded += (_, _) =>
        {
            try
            {
                var pt = barWin.PointToScreen(new Point(0, 0));
                ChromeScale.Apply(barStack, ChromeScale.ForScreenPoint(barWin, (int)pt.X, (int)pt.Y));
                barWin.UpdateLayout();
                barWin.Top = SystemParameters.VirtualScreenTop + Math.Max(4, _dipRegion.Top - barWin.ActualHeight - 4);
            }
            catch { /* ölçek başarısızsa varsayılan konum kalır */ }

            uiTimer.Start();
            blinkTimer.Start();
            UpdateRecordingVisuals();

            hookProc = (code, wparam, lparam) =>
            {
                if (code >= 0 && (wparam == WM_KEYDOWN || wparam == WM_SYSKEYDOWN))
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lparam);
                    var key = KeyInterop.KeyFromVirtualKey((int)info.vkCode);
                    if (key == Key.Escape)
                        barWin.Dispatcher.BeginInvoke(DoStop);
                    else if (key == Key.P && IsDown(VK_CONTROL) && IsDown(VK_SHIFT))
                        barWin.Dispatcher.BeginInvoke(TogglePause);
                }
                return CallNextHookEx(keyboardHook, code, wparam, lparam);
            };

            using var process = System.Diagnostics.Process.GetCurrentProcess();
            using var module = process.MainModule!;
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(module.ModuleName), 0);
        };

        barWin.Closed += (_, _) =>
        {
            uiTimer.Stop();
            blinkTimer.Stop();
            _session.LimitReached -= OnLimitReached;
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        };

        borderWin.Show();
        barWin.Show();
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static Button MakeBarButton(string text, Color background, double width) => new()
    {
        Content = text,
        Width = width,
        Height = 26,
        FontSize = 11,
        FontFamily = new WpfFontFamily("Segoe UI"),
        Background = new SolidColorBrush(background),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 4, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
}
