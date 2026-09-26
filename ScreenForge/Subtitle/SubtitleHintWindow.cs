using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleHintWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private readonly DispatcherTimer _timer;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public SubtitleHintWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Width = 420;
        Height = 36;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 31, 36, 48)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            Child = new TextBlock
            {
                Text = "Çift tıklayarak metin konumunu değiştirebilirsiniz.",
                Foreground = Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
            },
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Hide();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, ex | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };
    }

    public void ShowAbove(int x, int y, int w, int h)
    {
        int width = 420;
        int height = 36;
        int left = x + Math.Max(0, (w - width) / 2);
        int top = y - height - 8;
        if (top < 8)
            top = y + h + 8;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, new IntPtr(-1), left, top, width, height, SwpNoActivate);
        _timer.Stop();
        _timer.Start();
    }

    public new void Hide()
    {
        _timer.Stop();
        base.Hide();
    }
}
