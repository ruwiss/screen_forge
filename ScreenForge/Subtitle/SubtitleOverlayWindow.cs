using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenForge.Capture;
using ScreenForge.Settings;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleOverlayWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsThickFrame = 0x00040000;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int WmExitSizeMove = 0x0232;
    private const int MaActivate = 1;
    private const int MaNoActivate = 3;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private readonly SubtitleSettings _settings;
    private readonly TextBlock _text;
    private readonly Button _gear;
    private readonly Border _chrome;
    private SubtitleHintWindow? _hint;
    private SubtitleStyleWindow? _style;
    private string _live = "";
    private bool _editing;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);
    [DllImport("user32.dll")] private static extern int GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    public SubtitleOverlayWindow(SubtitleSettings settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        UseLayoutRounding = true;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 8, 12, 8),
            IsHitTestVisible = false,
        };
        _gear = new Button
        {
            Content = new TextBlock
            {
                Text = "\uE713",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Görünüm",
        };
        _gear.Click += (_, e) =>
        {
            e.Handled = true;
            ToggleStyle();
        };
        var grid = new Grid();
        grid.Children.Add(_text);
        grid.Children.Add(_gear);
        _chrome = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
        ApplyLook();
        Content = _chrome;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            ApplyChrome(clickThrough: true);
            PlaceFromSettings();
        };
        SizeChanged += (_, _) => _text.FontSize = Math.Clamp(ActualHeight / 4.2, 13, 22);
        LocationChanged += (_, _) => GuardSourceOverlap();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public bool ContainsScreen(int x, int y)
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return false;
        return x >= rect.Left && y >= rect.Top && x < rect.Right && y < rect.Bottom;
    }

    public bool Covers(int x, int y)
        => ContainsScreen(x, y) || (_style is { IsVisible: true } && _style.Contains(x, y));

    public bool IsEditing => _editing;

    public void ShowLive()
    {
        PlaceFromSettings();
        if (!IsVisible) Show();
        ExitEdit();
        KeepOnTop();
    }

    public void ShowEditor()
    {
        PlaceFromSettings();
        if (!IsVisible) Show();
        EnterEdit();
        KeepOnTop();
    }

    public void EnterEdit()
    {
        _editing = true;
        _gear.Visibility = Visibility.Visible;
        _text.Margin = new Thickness(12, 8, 30, 8);
        HideHint();
        ApplyLook();
        ApplyChrome(clickThrough: false);
        IsHitTestVisible = true;
        try { Activate(); } catch { /* tepsi oturumu */ }
        RefreshText();
        KeepOnTop();
    }

    public void ExitEdit()
    {
        _editing = false;
        _gear.Visibility = Visibility.Collapsed;
        _text.Margin = new Thickness(12, 8, 12, 8);
        _style?.Hide();
        ApplyLook();
        CommitBox();
        ApplyChrome(clickThrough: true);
        IsHitTestVisible = false;
        RefreshText();
    }

    public void SetLiveText(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetLiveText(text));
            return;
        }
        if (_live == text) return;
        _live = text;
        RefreshText();
    }

    public void ShowHint()
    {
        if (!GetWindowRect(Handle, out var rect) && _settings.HasBox)
        {
            _hint ??= new SubtitleHintWindow();
            _hint.ShowAbove(_settings.BoxX, _settings.BoxY, _settings.BoxW, _settings.BoxH);
            return;
        }
        if (!GetWindowRect(Handle, out rect)) return;
        _hint ??= new SubtitleHintWindow();
        _hint.ShowAbove(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public void HideHint() => _hint?.Hide();

    public void ApplyLook()
    {
        byte alpha = (byte)Math.Clamp(_settings.Opacity * 255, 38, 255);
        _chrome.Background = SubtitleStyleWindow.Brush(_settings.BackColor, alpha);
        double border = _editing && _settings.BorderThickness < 0.4 ? 1 : _settings.BorderThickness;
        _chrome.BorderThickness = new Thickness(border);
        _chrome.BorderBrush = border < 0.4
            ? Brushes.Transparent
            : SubtitleStyleWindow.Brush(_settings.BorderColor, alpha);
        if (!string.IsNullOrEmpty(_live))
            _text.Foreground = SubtitleStyleWindow.Brush(_settings.TextColor, 255);
    }

    private void ToggleStyle()
    {
        if (_style is { IsVisible: true })
        {
            _style.Hide();
            return;
        }
        _style ??= new SubtitleStyleWindow(_settings, ApplyLook);
        if (!GetWindowRect(Handle, out var rect))
            return;
        _style.ShowBelow(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public void KeepOnTop()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        _style?.Raise();
    }

    public void SetHiddenFromCapture(bool hidden)
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowDisplayAffinity(hwnd, hidden ? WdaExcludeFromCapture : 0);
    }

    private void RefreshText()
    {
        if (string.IsNullOrEmpty(_live))
        {
            _text.Text = _editing ? "Çeviri burada görünecek" : "";
            _text.Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xCB, 0xD6));
            return;
        }
        _text.Text = _live;
        ApplyLook();
    }

    private void PlaceFromSettings()
    {
        if (!_settings.HasBox) return;
        double sx = DpiX();
        double sy = DpiY();
        var virt = ScreenCapture.VirtualScreenBounds;
        Left = SystemParameters.VirtualScreenLeft + (_settings.BoxX - virt.X) / sx;
        Top = SystemParameters.VirtualScreenTop + (_settings.BoxY - virt.Y) / sy;
        Width = Math.Max(160, _settings.BoxW / sx);
        Height = Math.Max(36, _settings.BoxH / sy);
    }

    private double DpiX()
    {
        if (IsLoaded)
            return Math.Max(0.01, VisualTreeHelper.GetDpi(this).DpiScaleX);
        return Math.Max(0.01, GetDpiForSystem() / 96.0);
    }

    private double DpiY()
    {
        if (IsLoaded)
            return Math.Max(0.01, VisualTreeHelper.GetDpi(this).DpiScaleY);
        return Math.Max(0.01, GetDpiForSystem() / 96.0);
    }

    private void CommitBox()
    {
        if (!IsVisible || ActualWidth < 2 || ActualHeight < 2) return;
        try
        {
            var tl = PointToScreen(new Point(0, 0));
            var br = PointToScreen(new Point(ActualWidth, ActualHeight));
            int w = Math.Max(40, (int)Math.Round(br.X - tl.X));
            int h = Math.Max(24, (int)Math.Round(br.Y - tl.Y));
            _settings.SetBox((int)Math.Round(tl.X), (int)Math.Round(tl.Y), w, h);
            App.Settings.Save();
        }
        catch
        {
            /* pencere kapanırken */
        }
    }

    private void ApplyChrome(bool clickThrough)
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GwlStyle) & ~WsThickFrame;
        int ex = GetWindowLong(hwnd, GwlExStyle);
        ex |= WsExLayered | WsExToolWindow;
        if (clickThrough)
            ex |= WsExTransparent | WsExNoActivate;
        else
            ex &= ~(WsExTransparent | WsExNoActivate);
        SetWindowLong(hwnd, GwlStyle, style);
        SetWindowLong(hwnd, GwlExStyle, ex);
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmExitSizeMove && _editing)
        {
            CommitBox();
            return IntPtr.Zero;
        }

        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(_editing ? MaActivate : MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private bool _guardMove;
    private bool _sizing;
    private int _sizeEdge;
    private int _grabX, _grabY, _origL, _origT, _origW, _origH;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WinPoint point);

    private struct WinPoint { public int X, Y; }

    private void OnDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_editing) return;
        if (IsGear(e.OriginalSource as DependencyObject)) return;
        int edge = EdgeAt(e.GetPosition(this));
        if (edge < 0)
        {
            try
            {
                _guardMove = true;
                DragMove();
                _guardMove = false;
                NudgeOffSource();
                CommitBox();
            }
            catch
            {
                _guardMove = false;
            }
            e.Handled = true;
            return;
        }
        if (!GetCursorPos(out var cur) || !GetWindowRect(Handle, out var rect))
            return;
        _sizing = true;
        _sizeEdge = edge;
        _grabX = cur.X;
        _grabY = cur.Y;
        _origL = rect.Left;
        _origT = rect.Top;
        _origW = Math.Max(1, rect.Right - rect.Left);
        _origH = Math.Max(1, rect.Bottom - rect.Top);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_editing) return;
        if (!_sizing)
        {
            Cursor = CursorFor(EdgeAt(e.GetPosition(this)));
            return;
        }
        if (!GetCursorPos(out var cur)) return;
        int dx = cur.X - _grabX;
        int dy = cur.Y - _grabY;
        int left = _origL;
        int top = _origT;
        int w = _origW;
        int h = _origH;
        if (_sizeEdge is 1 or 4 or 6)
        {
            w = Math.Clamp(_origW - dx, 160, 1600);
            left = _origL + (_origW - w);
        }
        else if (_sizeEdge is 2 or 5 or 7)
            w = Math.Clamp(_origW + dx, 160, 1600);
        if (_sizeEdge is 3 or 4 or 5)
        {
            h = Math.Clamp(_origH - dy, 36, 480);
            top = _origT + (_origH - h);
        }
        else if (_sizeEdge is 6 or 7 or 8)
            h = Math.Clamp(_origH + dy, 36, 480);
        ApplyScreenRect(left, top, w, h);
    }

    private void ApplyScreenRect(int x, int y, int w, int h)
    {
        var cleared = ClearOfSource(x, y, w, h);
        x = cleared.X;
        y = cleared.Y;
        w = cleared.Width;
        h = cleared.Height;
        double sx = DpiX();
        double sy = DpiY();
        var virt = ScreenCapture.VirtualScreenBounds;
        Left = SystemParameters.VirtualScreenLeft + (x - virt.X) / sx;
        Top = SystemParameters.VirtualScreenTop + (y - virt.Y) / sy;
        Width = Math.Max(40, w / sx);
        Height = Math.Max(24, h / sy);
    }

    private void GuardSourceOverlap()
    {
        if (!_guardMove || _pushing) return;
        NudgeOffSource();
    }

    private void NudgeOffSource()
    {
        if (_pushing || !GetWindowRect(Handle, out var rect)) return;
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        var cleared = ClearOfSource(rect.Left, rect.Top, w, h);
        if (cleared.X == rect.Left && cleared.Y == rect.Top) return;
        _pushing = true;
        ApplyScreenRect(cleared.X, cleared.Y, cleared.Width, cleared.Height);
        _pushing = false;
    }

    private System.Drawing.Rectangle ClearOfSource(int x, int y, int w, int h)
    {
        var box = new System.Drawing.Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
        if (!_settings.HasSource) return box;
        var src = new System.Drawing.Rectangle(
            _settings.SourceX - 4,
            _settings.SourceY - 4,
            _settings.SourceW + 8,
            _settings.SourceH + 8);
        return SubtitleMath.PushOutside(box, src);
    }

    private bool _pushing;

    private static System.Windows.Input.Cursor CursorFor(int edge) => edge switch
    {
        1 or 2 => System.Windows.Input.Cursors.SizeWE,
        3 or 8 => System.Windows.Input.Cursors.SizeNS,
        4 or 7 => System.Windows.Input.Cursors.SizeNWSE,
        5 or 6 => System.Windows.Input.Cursors.SizeNESW,
        _ => System.Windows.Input.Cursors.SizeAll,
    };

    private void OnUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_sizing) return;
        _sizing = false;
        try { if (IsMouseCaptured) ReleaseMouseCapture(); } catch { /* ignore */ }
        CommitBox();
        e.Handled = true;
    }

    private int EdgeAt(Point p)
    {
        const double g = 10;
        bool l = p.X <= g;
        bool r = p.X >= ActualWidth - g;
        bool t = p.Y <= g;
        bool b = p.Y >= ActualHeight - g;
        if (t && l) return 4;
        if (t && r) return 5;
        if (b && l) return 6;
        if (b && r) return 7;
        if (l) return 1;
        if (r) return 2;
        if (t) return 3;
        if (b) return 8;
        return -1;
    }

    private bool IsGear(DependencyObject? src)
    {
        while (src != null)
        {
            if (ReferenceEquals(src, _gear)) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }
}
