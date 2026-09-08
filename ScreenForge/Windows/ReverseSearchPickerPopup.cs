using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenForge.Windows;

internal sealed class ReverseSearchPickerPopup
{
    private static readonly SolidColorBrush Text = new(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly SolidColorBrush Line = new(Color.FromRgb(0x3A, 0x42, 0x54));
    private static readonly SolidColorBrush Surface = new(Color.FromArgb(0xF2, 0x1F, 0x24, 0x30));

    private readonly Popup _popup;
    private readonly Window _host;
    private readonly Border _card;
    private bool _outsideHooked;

    public ReverseSearchPickerPopup(
        FrameworkElement placementTarget,
        Window host,
        Action onYandex,
        Action onGoogle)
    {
        _host = host;

        _popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 10,
            StaysOpen = true,
            AllowsTransparency = true,
        };

        var menu = new StackPanel { Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch };
        menu.Children.Add(ModeRow(StrokeIcon("IconYandex"), "Yandex Görseller", () => { Close(); onYandex(); }));
        menu.Children.Add(ModeRow(StrokeIcon("IconLens"), "Google Lens", () => { Close(); onGoogle(); }));

        _card = new Border
        {
            MinWidth = 172,
            Background = Surface,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = menu,
            SnapsToDevicePixels = true,
        };
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5,
        };

        _popup.Child = _card;
        _popup.Closed += (_, _) => UnhookOutside();
    }

    public void Open()
    {
        _popup.IsOpen = true;
        if (_popup.PlacementTarget is FrameworkElement target && target.ActualWidth > 0)
            _card.MinWidth = Math.Max(172, target.ActualWidth);
        HookOutside();
        if (_popup.Child is FrameworkElement el)
        {
            try
            {
                var pt = el.PointToScreen(new Point(0, 0));
                ChromeScale.Apply(el, ChromeScale.ForScreenPoint(el, (int)pt.X, (int)pt.Y));
            }
            catch { /* 1x */ }
        }
    }

    private void Close()
    {
        _popup.IsOpen = false;
        UnhookOutside();
    }

    private void HookOutside()
    {
        if (_outsideHooked) return;
        _host.PreviewMouseLeftButtonDown += OnHostDown;
        _outsideHooked = true;
    }

    private void UnhookOutside()
    {
        if (!_outsideHooked) return;
        _host.PreviewMouseLeftButtonDown -= OnHostDown;
        _outsideHooked = false;
    }

    private void OnHostDown(object sender, MouseButtonEventArgs e)
    {
        if (!_popup.IsOpen) return;
        if (_popup.Child is IInputElement child && child.IsMouseOver) return;
        Close();
    }

    private static Border ModeRow(UIElement icon, string title, Action start)
    {
        icon.SetValue(FrameworkElement.WidthProperty, 14d);
        icon.SetValue(FrameworkElement.HeightProperty, 14d);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var grid = new Grid { Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconWrap = new Border
        {
            Padding = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        };
        Grid.SetColumn(iconWrap, 0);
        Grid.SetColumn(label, 1);
        grid.Children.Add(iconWrap);
        grid.Children.Add(label);

        var hit = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(5),
        };
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            start();
        };
        return hit;
    }

    private static Path StrokeIcon(string key, double size = 16) => new()
    {
        Data = Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty,
        Stroke = Text,
        StrokeThickness = 1.6,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };
}
