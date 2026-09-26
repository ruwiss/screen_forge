using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenForge.Settings;

namespace ScreenForge.Windows;

internal sealed class VisualTranslatePickerPopup
{
    private static readonly SolidColorBrush Text = new(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x9A, 0xA4, 0xB8));
    private static readonly SolidColorBrush Line = new(Color.FromRgb(0x3A, 0x42, 0x54));
    private static readonly SolidColorBrush Surface = new(Color.FromArgb(0xF2, 0x1F, 0x24, 0x30));
    private static readonly SolidColorBrush Selected = new(Color.FromRgb(0x2A, 0x20, 0x10));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0xEA, 0x6F, 0x12));

    private readonly Popup _popup;
    private readonly Window _host;
    private readonly Border _card;
    private readonly StackPanel _allHost;
    private readonly ScrollViewer _allScroll;
    private readonly FrameworkElement _allChevron;
    private bool _outsideHooked;
    private bool _allOpen;

    public VisualTranslatePickerPopup(
        FrameworkElement placementTarget,
        Window host,
        string defaultCode,
        IReadOnlyList<string> recent,
        Action<string> onPick)
    {
        _host = host;
        _popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 8,
            StaysOpen = true,
            AllowsTransparency = true,
        };

        var menu = new StackPanel { Margin = new Thickness(4) };
        menu.Children.Add(LangRow(defaultCode, selected: true, () => Pick(defaultCode, onPick)));

        if (recent.Count > 0)
        {
            menu.Children.Add(Section("Son kullanılanlar"));
            foreach (var code in recent)
                menu.Children.Add(LangRow(code, selected: false, () => Pick(code, onPick)));
        }

        _allChevron = ChevronHost();
        var allToggle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 2, 0, 0),
            Child = HeaderRow(_allChevron, "Tüm diller", Muted),
        };
        allToggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleAll();
        };
        menu.Children.Add(allToggle);

        _allHost = new StackPanel();
        foreach (var (code, _) in TranslateLanguageDefaults.Languages)
            _allHost.Children.Add(LangRow(code, selected: false, () => Pick(code, onPick)));
        _allScroll = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _allHost,
            Visibility = Visibility.Collapsed,
        };
        menu.Children.Add(_allScroll);

        _card = new Border
        {
            MinWidth = 196,
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

    public bool IsOpen => _popup.IsOpen;

    public void Close() => _popup.IsOpen = false;

    public void Open()
    {
        _popup.IsOpen = true;
        if (_popup.PlacementTarget is FrameworkElement target && target.ActualWidth > 0)
            _card.MinWidth = Math.Max(196, target.ActualWidth);
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

    private void Pick(string code, Action<string> onPick)
    {
        _popup.IsOpen = false;
        onPick(code);
    }

    private void ToggleAll()
    {
        _allOpen = !_allOpen;
        _allScroll.Visibility = _allOpen ? Visibility.Visible : Visibility.Collapsed;
        _allChevron.RenderTransform = new RotateTransform(_allOpen ? 180 : 0);
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
        if (IsOnPlacement(e.OriginalSource as DependencyObject)) return;
        _popup.IsOpen = false;
    }

    private bool IsOnPlacement(DependencyObject? src)
    {
        var target = _popup.PlacementTarget as DependencyObject;
        while (src != null)
        {
            if (ReferenceEquals(src, target))
                return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 10,
        Foreground = Muted,
        Margin = new Thickness(8, 8, 8, 2),
    };

    private static Border LangRow(string code, bool selected, Action pick)
    {
        var row = new Border
        {
            Background = selected ? Selected : Brushes.Transparent,
            Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(5),
            Child = HeaderRow(selected ? Check() : Spacer(), TranslateLanguageDefaults.Label(code), Text),
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            pick();
        };
        row.MouseEnter += (_, _) =>
        {
            if (!selected)
                row.Background = new SolidColorBrush(Color.FromRgb(0x27, 0x2D, 0x3B));
        };
        row.MouseLeave += (_, _) =>
        {
            if (!selected)
                row.Background = Brushes.Transparent;
        };
        return row;
    }

    private static Grid HeaderRow(UIElement leading, string title, Brush foreground)
    {
        var grid = new Grid { Height = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lead = new Border
        {
            Width = 16,
            Height = 16,
            Child = leading,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(lead);
        grid.Children.Add(label);
        return grid;
    }

    private static UIElement Check()
    {
        var geo = Application.Current?.TryFindResource("IconCheck") as Geometry;
        return IconBox(new Path
        {
            Data = geo ?? Geometry.Parse("M3 7 L6 10 L11 4"),
            Stroke = Accent,
            StrokeThickness = 1.8,
            Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private static UIElement Spacer() => new Border { Width = 16, Height = 16 };

    private static FrameworkElement ChevronHost()
    {
        var box = IconBox(new Path
        {
            Data = Geometry.Parse("M2 7 L8 13 L14 7"),
            Stroke = Muted,
            StrokeThickness = 1.8,
            Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        box.RenderTransformOrigin = new Point(0.5, 0.5);
        return box;
    }

    private static Viewbox IconBox(UIElement icon) => new()
    {
        Width = 12,
        Height = 12,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = icon,
    };
}
