using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenForge.Settings;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleStyleWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private readonly SubtitleSettings _settings;
    private readonly Action _changed;
    private bool _loading = true;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    public SubtitleStyleWindow(SubtitleSettings settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 228;
        SizeToContent = SizeToContent.Height;
        Content = Build();
        _loading = false;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, ex | WsExToolWindow);
        };
    }

    public bool Contains(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return false;
        return x >= rect.Left && y >= rect.Top && x < rect.Right && y < rect.Bottom;
    }

    public void ShowBelow(int boxLeft, int boxTop, int boxW, int boxH)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        int w = 228;
        int h = (int)Math.Ceiling(ActualHeight);
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(boxLeft, boxTop, Math.Max(1, boxW), Math.Max(1, boxH)));
        var area = screen.WorkingArea;
        int x = Math.Clamp(boxLeft + boxW - w, area.Left + 8, Math.Max(area.Left + 8, area.Right - w - 8));
        int y = boxTop + boxH + 8;
        if (h > 40 && y + h > area.Bottom - 8)
            y = Math.Max(area.Top + 8, boxTop - h - 8);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, new IntPtr(-1), x, y, w, Math.Max(h, 1), h > 40 ? 0x0010u : 0x0011u);
        Raise();
    }

    public void Raise()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    private UIElement Build()
    {
        var stack = new StackPanel { Margin = new Thickness(8, 8, 4, 8) };
        stack.Children.Add(Row(SubtitleThemes.All));
        stack.Children.Add(OpacityRow());
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = stack,
        };
    }

    private UniformGrid Row(IReadOnlyList<SubtitleTheme> themes)
    {
        var row = new UniformGrid { Columns = 4 };
        foreach (var theme in themes)
            row.Children.Add(Card(theme));
        return row;
    }

    private Button Card(SubtitleTheme theme)
    {
        bool on = string.Equals(_settings.Theme, theme.Id, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Height = 28,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            ToolTip = theme.Name,
            Background = Brush(theme.Back, 255),
            Foreground = Brush(theme.Text, 255),
            BorderBrush = on
                ? new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12))
                : Brush(theme.Text, 180),
            BorderThickness = new Thickness(on ? 2 : 1),
            Content = new TextBlock
            {
                Text = "Aa",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += (_, _) =>
        {
            theme.Apply(_settings);
            Touch();
            Content = Build();
        };
        return button;
    }

    private StackPanel OpacityRow()
    {
        var slider = new Slider
        {
            Minimum = 0.15,
            Maximum = 1,
            Value = _settings.Opacity,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
        };
        slider.ValueChanged += (_, e) =>
        {
            _settings.Opacity = e.NewValue;
            Touch();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 4, 0) };
        row.Children.Add(new TextBlock
        {
            Text = "Saydam",
            Width = 52,
            Foreground = Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(slider);
        return row;
    }

    private void Touch()
    {
        if (_loading) return;
        _settings.Normalize();
        App.Settings.Save();
        _changed();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xCB, 0xD6)),
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        FontSize = 11,
        Margin = new Thickness(0, 8, 0, 4),
    };

    internal static SolidColorBrush Brush(string hex, byte alpha)
    {
        var color = Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    internal static Color Parse(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && System.Windows.Media.ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch
        {
            /* bozuk renk */
        }
        return Colors.White;
    }
}
