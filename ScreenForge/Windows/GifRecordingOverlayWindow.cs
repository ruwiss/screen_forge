using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenForge.Gif;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// Transparent full-screen overlay shown during GIF recording.
/// Shows a dashed border around the capture region + a control bar above it.
/// No screen dimming — the recording area is fully visible.
/// </summary>
public sealed class GifRecordingOverlayWindow
{
    // ─── Global Keyboard Hook (WH_KEYBOARD_LL = 13) ──────────────────────────
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    public event Action<GifRecorder>? Stopped;

    private readonly GifRecorder _recorder;
    private readonly Rect _dipRegion;

    public GifRecordingOverlayWindow(GifRecorder recorder, Rect dipRegion)
    {
        _recorder = recorder;
        _dipRegion = dipRegion;
    }

    public void Show()
    {
        Window? win = null;
        TextBlock? elapsedText = null;
        TextBlock? frameText = null;
        TextBlock? recDot = null;
        IntPtr keyboardHook = IntPtr.Zero;
        LowLevelKeyboardProc? hookProc = null;

        // ─── Dashed border (hit-test off — sadece görsel) ────────────────────
        var borderRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            StrokeDashCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Width = _dipRegion.Width,
            Height = _dipRegion.Height,
        };

        // ─── Control bar ─────────────────────────────────────────────────────
        recDot = new TextBlock
        {
            Text = "●",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };

        elapsedText = new TextBlock
        {
            Text = "00:00",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        frameText = new TextBlock
        {
            Text = "0 kare",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var stopBtn = new Button
        {
            Content = "Durdur",
            Width = 72, Height = 26,
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Background = new SolidColorBrush(Color.FromRgb(0xBE, 0x3A, 0x3A)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var barStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        barStack.Children.Add(recDot);
        barStack.Children.Add(elapsedText);
        barStack.Children.Add(frameText);
        barStack.Children.Add(stopBtn);

        var controlBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x24, 0x32)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Child = barStack,
        };

        // ─── Canvas — Canvas kendisi IsHitTestVisible=true kalır ─────────────
        // borderRect.IsHitTestVisible=false (ayarlandı), controlBar default true
        var rootCanvas = new Canvas { Background = Brushes.Transparent };
        rootCanvas.Children.Add(borderRect);
        rootCanvas.Children.Add(controlBar);

        Canvas.SetLeft(borderRect, _dipRegion.Left);
        Canvas.SetTop(borderRect, _dipRegion.Top);
        Canvas.SetLeft(controlBar, _dipRegion.Left);
        Canvas.SetTop(controlBar, Math.Max(0, _dipRegion.Top - 44));

        win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Content = rootCanvas,
        };

        // ─── Hide/Show hook — overlay BitBlt karesine girmesin ───────────────
        _recorder.HideForCapture = () => win!.Dispatcher.Invoke(() =>
        {
            win.Visibility = Visibility.Hidden;
        });
        _recorder.ShowAfterCapture = () => win!.Dispatcher.Invoke(() =>
        {
            win.Visibility = Visibility.Visible;
        });

        // ─── Stop ─────────────────────────────────────────────────────────────
        void DoStop()
        {
            _recorder.Stop();
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            win!.Close();
            Stopped?.Invoke(_recorder);
        }

        stopBtn.Click += (_, _) => DoStop();

        // ─── Timers ───────────────────────────────────────────────────────────
        var uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        uiTimer.Tick += (_, _) =>
        {
            var e = _recorder.Elapsed;
            elapsedText!.Text = $"{(int)e.TotalMinutes:D2}:{e.Seconds:D2}";
            frameText!.Text = $"{_recorder.FrameCount} kare";
        };

        var blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool blinkOn = true;
        blinkTimer.Tick += (_, _) => { blinkOn = !blinkOn; recDot!.Opacity = blinkOn ? 1.0 : 0.2; };

        win.Loaded += (_, _) =>
        {
            uiTimer.Start();
            blinkTimer.Start();

            // Global klavye hook — kayıt sırasında basılan tuşları kaydet
            hookProc = (code, wp, lp) =>
            {
                if (code >= 0 && (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN))
                {
                    var kbs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lp);
                    var key = KeyInterop.KeyFromVirtualKey((int)kbs.vkCode);
                    var label = GetKeyLabel(key);
                    if (label != null) _recorder.RecordKey(label);
                }
                return CallNextHookEx(keyboardHook, code, wp, lp);
            };

            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod = proc.MainModule!;
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(mod.ModuleName), 0);
        };

        win.Closed += (_, _) =>
        {
            uiTimer.Stop();
            blinkTimer.Stop();
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        };

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DoStop();
        };

        win.Show();
    }

    private static string? GetKeyLabel(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin or Key.RWin => "Win",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Tab => "Tab",
        Key.Escape => null, // Esc durdurur, kaydetme
        Key.Space => "Space",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => key.ToString().TrimStart('D'),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        _ => null,
    };
}
