using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FontFamily = System.Windows.Media.FontFamily;

namespace ScreenForge.Windows;

/// <summary>
/// Overlay kapanınca yaşayan kısa durum bildirimi (OCR progress/sonuç).
/// </summary>
internal sealed class StatusToastWindow : Window
{
    private readonly TextBlock _text;
    private readonly Grid _spinner;
    private readonly RotateTransform _spin;
    private DispatcherTimer? _autoClose;

    public StatusToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;

        _spin = new RotateTransform();
        var arc = new Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
            StrokeThickness = 2.2,
            StrokeDashArray = new DoubleCollection { 3.2, 2.2 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _spin,
        };
        _spinner = new Grid
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _spinner.Children.Add(arc);

        _text = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF0, 0xF7)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_spinner);
        row.Children.Add(_text);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE2, 0x1F, 0x24, 0x30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54)),
            BorderThickness = new Thickness(1),
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.45,
            },
        };

        Loaded += (_, _) =>
        {
            try
            {
                var pt = PointToScreen(new Point(0, 0));
                if (Content is FrameworkElement el)
                    ChromeScale.Apply(el, ChromeScale.ForScreenPoint(this, (int)pt.X, (int)pt.Y));
                UpdateLayout();
            }
            catch { /* 1x */ }
            SnapToWorkArea();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        };
    }

    public void ShowBusy(string message)
    {
        _autoClose?.Stop();
        _text.Text = message;
        _spinner.Visibility = Visibility.Visible;
        _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
        if (!IsVisible) Show();
        else SnapToWorkArea();
    }

    public void ShowResult(string message, int autoCloseMs = 1600)
    {
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        _spin.Angle = 0;
        _spinner.Visibility = Visibility.Collapsed;
        _text.Text = message;
        if (!IsVisible) Show();
        else
        {
            UpdateLayout();
            SnapToWorkArea();
        }

        _autoClose?.Stop();
        if (autoCloseMs <= 0) return;
        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            Close();
        };
        _autoClose.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoClose?.Stop();
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        base.OnClosed(e);
    }

    private void SnapToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        double w = ActualWidth;
        double h = ActualHeight;
        if (Content is FrameworkElement el)
        {
            var sz = ChromeScale.LayoutSize(el);
            if (sz.Width > 0) w = sz.Width;
            if (sz.Height > 0) h = sz.Height;
        }
        Left = wa.Left + (wa.Width - w) / 2;
        Top = wa.Bottom - h - 28;
    }
}
