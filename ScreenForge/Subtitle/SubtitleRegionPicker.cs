using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenForge.Capture;
using ScreenForge.Windows;
using WpfRect = System.Windows.Rect;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleRegionPicker : Window
{
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly WpfRectangle _dimTop = Dim();
    private readonly WpfRectangle _dimLeft = Dim();
    private readonly WpfRectangle _dimRight = Dim();
    private readonly WpfRectangle _dimBottom = Dim();
    private readonly WpfRectangle _frame = new()
    {
        Stroke = Brushes.White,
        StrokeThickness = 2,
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
    private bool _drag;
    private Point _start;
    private bool _done;

    public event Action<int, int, int, int>? Completed;
    public event Action? Cancelled;

    public SubtitleRegionPicker()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        ResizeMode = ResizeMode.NoResize;
        Cursor = WhiteCrossCursor.Instance;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 31, 36, 48)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Çevrilecek altyazı bölgesini seçin",
                Foreground = Brushes.White,
                FontSize = 15,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            },
        };

        _canvas.Children.Add(_dimTop);
        _canvas.Children.Add(_dimLeft);
        _canvas.Children.Add(_dimRight);
        _canvas.Children.Add(_dimBottom);
        _canvas.Children.Add(_frame);
        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(hint);
        Content = root;

        Loaded += (_, _) =>
        {
            FillDim(WpfRect.Empty);
            Activate();
            Focus();
        };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Cancel();
        };
        Closed += (_, _) =>
        {
            if (_done) return;
            _done = true;
            Cancelled?.Invoke();
        };
    }

    private static WpfRectangle Dim() => new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
        IsHitTestVisible = false,
    };

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _drag = true;
        _start = e.GetPosition(this);
        CaptureMouse();
        UpdateSelection(new WpfRect(_start.X, _start.Y, 0, 0));
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_drag) return;
        UpdateSelection(RectFrom(_start, e.GetPosition(this)));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        var dip = RectFrom(_start, e.GetPosition(this));
        if (dip.Width < 4 || dip.Height < 4)
        {
            UpdateSelection(WpfRect.Empty);
            return;
        }

        var tl = PointToScreen(new Point(dip.X, dip.Y));
        var br = PointToScreen(new Point(dip.Right, dip.Bottom));
        int x = (int)Math.Round(Math.Min(tl.X, br.X));
        int y = (int)Math.Round(Math.Min(tl.Y, br.Y));
        int w = (int)Math.Round(Math.Abs(br.X - tl.X));
        int h = (int)Math.Round(Math.Abs(br.Y - tl.Y));
        var virt = ScreenCapture.VirtualScreenBounds;
        var rect = System.Drawing.Rectangle.Intersect(new System.Drawing.Rectangle(x, y, w, h), virt);
        if (rect.Width < 8 || rect.Height < 8)
        {
            UpdateSelection(WpfRect.Empty);
            return;
        }

        _done = true;
        Completed?.Invoke(rect.X, rect.Y, rect.Width, rect.Height);
        Close();
    }

    private void Cancel()
    {
        if (_done) return;
        _done = true;
        Cancelled?.Invoke();
        Close();
    }

    private void UpdateSelection(WpfRect sel)
    {
        if (sel.Width < 1 || sel.Height < 1)
        {
            _frame.Visibility = Visibility.Collapsed;
            FillDim(WpfRect.Empty);
            return;
        }

        _frame.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, sel.X);
        Canvas.SetTop(_frame, sel.Y);
        _frame.Width = sel.Width;
        _frame.Height = sel.Height;
        FillDim(sel);
    }

    private void FillDim(WpfRect sel)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (sel.Width < 1 || sel.Height < 1)
        {
            Place(_dimTop, 0, 0, w, h);
            Place(_dimLeft, 0, 0, 0, 0);
            Place(_dimRight, 0, 0, 0, 0);
            Place(_dimBottom, 0, 0, 0, 0);
            return;
        }

        Place(_dimTop, 0, 0, w, sel.Y);
        Place(_dimLeft, 0, sel.Y, sel.X, sel.Height);
        Place(_dimRight, sel.Right, sel.Y, Math.Max(0, w - sel.Right), sel.Height);
        Place(_dimBottom, 0, sel.Bottom, w, Math.Max(0, h - sel.Bottom));
    }

    private static void Place(WpfRectangle rect, double x, double y, double w, double h)
    {
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(0, w);
        rect.Height = Math.Max(0, h);
    }

    private static WpfRect RectFrom(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new WpfRect(x, y, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }
}
