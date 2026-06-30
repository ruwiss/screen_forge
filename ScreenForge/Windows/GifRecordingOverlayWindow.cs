using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenForge.Gif;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// GIF kayıt sırasında gösterilen overlay.
/// İki pencere:
///   (1) Tam ekran şeffaf dashed border — WS_EX_TRANSPARENT + WDA_EXCLUDEFROMCAPTURE
///   (2) Küçük opaque (AllowsTransparency=false) control bar — tıklanabilir
/// WDA_EXCLUDEFROMCAPTURE her iki pencereyi BitBlt'den gizler; hide/show gerekmez.
/// </summary>
public sealed class GifRecordingOverlayWindow
{
    // ─── Win32 ───────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WH_KEYBOARD_LL    = 13;
    private const int WM_KEYDOWN        = 0x0100;
    private const int WM_SYSKEYDOWN     = 0x0104;
    // Pencerenin BitBlt/PrintScreen'de görünmemesini sağlar (Windows 10 2004+)
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    public event Action<GifRecorder>? Stopped;

    private readonly GifRecorder _recorder;
    private readonly Rect        _dipRegion;

    public GifRecordingOverlayWindow(GifRecorder recorder, Rect dipRegion)
    {
        _recorder  = recorder;
        _dipRegion = dipRegion;
    }

    public void Show()
    {
        Window? borderWin = null;
        Window? barWin    = null;
        IntPtr  kbHook    = IntPtr.Zero;
        LowLevelKeyboardProc? hookProc = null;

        // ═══ Pencere 1: tam ekran dashed border ═══════════════════════════════
        var dashRect = new WpfRectangle
        {
            Stroke          = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 6, 3 },
            StrokeDashCap   = PenLineCap.Round,
            Fill            = Brushes.Transparent,
            IsHitTestVisible = false,
            Width  = _dipRegion.Width,
            Height = _dipRegion.Height,
        };
        var borderCanvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        borderCanvas.Children.Add(dashRect);
        Canvas.SetLeft(dashRect, _dipRegion.Left);
        Canvas.SetTop(dashRect,  _dipRegion.Top);

        borderWin = new Window
        {
            WindowStyle        = WindowStyle.None,
            AllowsTransparency = true,
            Background         = Brushes.Transparent,
            Topmost            = true,
            ShowInTaskbar      = false,
            IsHitTestVisible   = false,
            Left  = SystemParameters.VirtualScreenLeft,
            Top   = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height= SystemParameters.VirtualScreenHeight,
            Content= borderCanvas,
        };
        borderWin.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(borderWin).Handle;
            // WS_EX_TRANSPARENT: mouse mesajları bu pencereye iletilmez
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            // WDA_EXCLUDEFROMCAPTURE: BitBlt'de görünmez
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };

        // ═══ Pencere 2: control bar (OPAQUE — kesinlikle tıklanabilir) ════════
        var recDot = new TextBlock
        {
            Text   = "●",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var elapsedText = new TextBlock
        {
            Text       = "00:00",
            Foreground = Brushes.White,
            FontSize   = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var frameText = new TextBlock
        {
            Text       = "0 kare",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            FontSize   = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var stopBtn = new Button
        {
            Content     = "Durdur",
            Width       = 72, Height = 26,
            FontSize    = 11,
            FontFamily  = new WpfFontFamily("Segoe UI"),
            Background  = new SolidColorBrush(Color.FromRgb(0xBE, 0x3A, 0x3A)),
            Foreground  = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor      = Cursors.Hand,
            Padding     = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var barStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 5, 10, 5),
        };
        barStack.Children.Add(recDot);
        barStack.Children.Add(elapsedText);
        barStack.Children.Add(frameText);
        barStack.Children.Add(stopBtn);

        double barLeft = SystemParameters.VirtualScreenLeft + _dipRegion.Left;
        double barTop  = SystemParameters.VirtualScreenTop  + Math.Max(4, _dipRegion.Top - 44);

        // AllowsTransparency=FALSE → normal opaque window → click-through yok
        barWin = new Window
        {
            WindowStyle        = WindowStyle.None,
            AllowsTransparency = false,
            Background         = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x32)),
            Topmost            = true,
            ShowInTaskbar      = false,
            SizeToContent      = SizeToContent.WidthAndHeight,
            Left               = barLeft,
            Top                = barTop,
            Content            = barStack,
        };
        barWin.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(barWin).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };

        // ─── HideForCapture artık gerekmiyor — WDA_EXCLUDEFROMCAPTURE halleder
        _recorder.HideForCapture    = null;
        _recorder.ShowAfterCapture  = null;

        // ─── Stop ─────────────────────────────────────────────────────────────
        void DoStop()
        {
            _recorder.Stop();
            if (kbHook != IntPtr.Zero) { UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
            borderWin!.Close();
            barWin!.Close();
            Stopped?.Invoke(_recorder);
        }
        stopBtn.Click += (_, _) => DoStop();
        barWin.KeyDown += (_, e) => { if (e.Key == Key.Escape) DoStop(); };

        // ─── Timers ───────────────────────────────────────────────────────────
        var uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        uiTimer.Tick += (_, _) =>
        {
            var el = _recorder.Elapsed;
            elapsedText.Text = $"{(int)el.TotalMinutes:D2}:{el.Seconds:D2}";
            frameText.Text   = $"{_recorder.FrameCount} kare";
        };
        var blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool blinkOn = true;
        blinkTimer.Tick += (_, _) => { blinkOn = !blinkOn; recDot.Opacity = blinkOn ? 1.0 : 0.2; };

        barWin.Loaded += (_, _) =>
        {
            uiTimer.Start();
            blinkTimer.Start();

            // Global klavye hook
            hookProc = (code, wp, lp) =>
            {
                if (code >= 0 && (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN))
                {
                    var kbs   = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lp);
                    var key   = KeyInterop.KeyFromVirtualKey((int)kbs.vkCode);
                    var label = GetKeyLabel(key);
                    if (label != null) _recorder.RecordKey(label);
                }
                return CallNextHookEx(kbHook, code, wp, lp);
            };
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod  = proc.MainModule!;
            kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(mod.ModuleName), 0);
        };
        barWin.Closed += (_, _) =>
        {
            uiTimer.Stop();
            blinkTimer.Stop();
            if (kbHook != IntPtr.Zero) { UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
        };

        borderWin.Show();
        barWin.Show();
    }

    private static string? GetKeyLabel(Key key) => key switch
    {
        Key.LeftCtrl  or Key.RightCtrl  => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt   or Key.RightAlt   => "Alt",
        Key.LWin      or Key.RWin       => "Win",
        Key.Return   => "Enter",
        Key.Back     => "Backspace",
        Key.Delete   => "Del",
        Key.Tab      => "Tab",
        Key.Escape   => null,
        Key.Space    => "Space",
        Key.Left     => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓",
        >= Key.A and <= Key.Z    => key.ToString(),
        >= Key.D0 and <= Key.D9  => key.ToString().TrimStart('D'),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        _ => null,
    };
}
