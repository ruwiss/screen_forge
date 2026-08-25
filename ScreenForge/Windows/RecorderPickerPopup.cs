using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenForge.Record;
using ScreenForge.Settings;

namespace ScreenForge.Windows;

internal sealed class RecorderPickerPopup
{
    private static readonly SolidColorBrush Text = new(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x9A, 0xA4, 0xB8));
    private static readonly SolidColorBrush Line = new(Color.FromRgb(0x3A, 0x42, 0x54));
    private static readonly SolidColorBrush Record = new(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly SolidColorBrush Surface = new(Color.FromArgb(0xF8, 0x1F, 0x24, 0x30));

    private readonly Popup _popup;
    private readonly AppSettings _settings;
    private readonly StackPanel _menu;
    private readonly StackPanel _gifSettings;
    private readonly StackPanel _videoSettings;
    private readonly Window _host;
    private readonly Border _card;
    private bool _outsideHooked;

    public RecorderPickerPopup(
        FrameworkElement placementTarget,
        Window host,
        AppSettings settings,
        Action onGif,
        Action onVideo)
    {
        _settings = settings;
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
        _gifSettings = BuildGifSettings();
        _videoSettings = BuildVideoSettings();
        _gifSettings.Visibility = Visibility.Collapsed;
        _videoSettings.Visibility = Visibility.Collapsed;

        _menu = new StackPanel { Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch };
        _menu.Children.Add(ModeRow(
            RecordDot(), "GIF",
            () => { Close(); onGif(); },
            () => Show(_gifSettings)));
        _menu.Children.Add(ModeRow(
            StrokeIcon("IconCamera"), "Ekran kaydı",
            () => { Close(); onVideo(); },
            () => Show(_videoSettings)));

        var root = new StackPanel();
        root.Children.Add(_menu);
        root.Children.Add(_gifSettings);
        root.Children.Add(_videoSettings);

        _card = new Border
        {
            MinWidth = 172,
            Background = Surface,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = root,
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

    private void Show(StackPanel panel)
    {
        _menu.Visibility = Visibility.Collapsed;
        _gifSettings.Visibility = Visibility.Collapsed;
        _videoSettings.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void ShowMenu()
    {
        _gifSettings.Visibility = Visibility.Collapsed;
        _videoSettings.Visibility = Visibility.Collapsed;
        _menu.Visibility = Visibility.Visible;
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

    private Border ModeRow(UIElement icon, string title, Action start, Action settings)
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

        var gearIcon = StrokeIcon("IconSettings", 12);
        gearIcon.HorizontalAlignment = HorizontalAlignment.Center;
        gearIcon.VerticalAlignment = VerticalAlignment.Center;
        var gear = new Border
        {
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            ToolTip = "Ayarlar",
            Child = gearIcon,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        gear.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            settings();
        };

        var grid = new Grid { Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconWrap = new Border
        {
            Padding = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        };
        Grid.SetColumn(iconWrap, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(gear, 2);
        grid.Children.Add(iconWrap);
        grid.Children.Add(label);
        grid.Children.Add(gear);

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
            if (e.Handled) return;
            e.Handled = true;
            start();
        };
        return hit;
    }

    private static Ellipse RecordDot() => new()
    {
        Width = 11,
        Height = 11,
        Fill = Record,
        Stroke = Brushes.White,
        StrokeThickness = 1.2,
    };

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

    private StackPanel BuildGifSettings()
    {
        var g = _settings.Gif;
        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
        panel.Children.Add(BackHeader("GIF ayarları"));
        panel.Children.Add(Labeled("FPS", FpsCombo(new[] { 5, 10, 12, 15, 20, 24, 25, 30 }, g.Fps, n =>
        {
            g.Fps = n;
            _settings.Save();
        })));
        panel.Children.Add(Check("İmleç", g.CaptureCursor, on => { g.CaptureCursor = on; _settings.Save(); }));
        return panel;
    }

    private StackPanel BuildVideoSettings()
    {
        var v = _settings.Video;
        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
        panel.Children.Add(BackHeader("Ekran kaydı ayarları"));
        panel.Children.Add(Labeled("FPS", FpsCombo(new[] { 15, 24, 30, 60 }, v.Fps, n =>
        {
            v.Fps = n;
            _settings.Save();
        })));
        panel.Children.Add(Labeled("Kalite", QualityCombo()));
        panel.Children.Add(Check("İmleç", v.CaptureCursor, on => { v.CaptureCursor = on; _settings.Save(); }));
        panel.Children.Add(Check("Tıklama vurgusu", v.HighlightClicks, on => { v.HighlightClicks = on; _settings.Save(); }));
        panel.Children.Add(Check("3 sn geri sayım", v.ShowCountdown, on => { v.ShowCountdown = on; _settings.Save(); }));
        panel.Children.Add(Check("Sistem sesi", v.RecordSystemAudio, on => { v.RecordSystemAudio = on; _settings.Save(); }));
        panel.Children.Add(Check("Mikrofon", v.RecordMicrophone, on => { v.RecordMicrophone = on; _settings.Save(); }));
        panel.Children.Add(Labeled("Cihaz", MicCombo()));
        return panel;
    }

    private Button BackHeader(string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(StrokeIcon("IconUndo", 13));
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var btn = new Button
        {
            Content = row,
            Height = 32,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 0, 0, 6),
            Style = Application.Current?.TryFindResource("ActionChip") as Style,
        };
        btn.Click += (_, _) => ShowMenu();
        return btn;
    }

    private static DockPanel Labeled(string label, UIElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
        var text = new TextBlock
        {
            Text = label,
            Width = 64,
            FontSize = 12,
            Foreground = Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(field);
        return row;
    }

    private ComboBox FpsCombo(int[] values, int selected, Action<int> set)
    {
        var cmb = Combo();
        int pick = 0;
        for (int i = 0; i < values.Length; i++)
        {
            cmb.Items.Add(new ComboBoxItem { Content = $"{values[i]} fps", Tag = values[i] });
            if (values[i] == selected) pick = i;
        }
        cmb.SelectedIndex = pick;
        cmb.SelectionChanged += (_, _) =>
        {
            if (cmb.SelectedItem is ComboBoxItem item && item.Tag is int n)
                set(n);
        };
        return cmb;
    }

    private ComboBox QualityCombo()
    {
        var cmb = Combo();
        cmb.Items.Add(new ComboBoxItem { Content = "Düşük", Tag = VideoQuality.Low });
        cmb.Items.Add(new ComboBoxItem { Content = "Orta", Tag = VideoQuality.Medium });
        cmb.Items.Add(new ComboBoxItem { Content = "Yüksek", Tag = VideoQuality.High });
        cmb.SelectedIndex = _settings.Video.Quality switch
        {
            VideoQuality.Low => 0,
            VideoQuality.High => 2,
            _ => 1,
        };
        cmb.SelectionChanged += (_, _) =>
        {
            if (cmb.SelectedItem is ComboBoxItem item && item.Tag is VideoQuality q)
            {
                _settings.Video.Quality = q;
                _settings.Save();
            }
        };
        return cmb;
    }

    private ComboBox MicCombo()
    {
        var cmb = Combo();
        var devices = AudioDevices.ListMicrophones();
        if (devices.Count == 0)
        {
            cmb.Items.Add(new ComboBoxItem { Content = "Mikrofon yok", Tag = "" });
            cmb.SelectedIndex = 0;
            return cmb;
        }

        int pick = 0;
        string want = _settings.Video.MicDeviceId;
        for (int i = 0; i < devices.Count; i++)
        {
            cmb.Items.Add(new ComboBoxItem { Content = devices[i].Name, Tag = devices[i].Id });
            if (devices[i].Id == want) pick = i;
        }
        cmb.SelectedIndex = pick;
        cmb.SelectionChanged += (_, _) =>
        {
            if (cmb.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                _settings.Video.MicDeviceId = id;
                _settings.Save();
            }
        };
        return cmb;
    }

    private static CheckBox Check(string label, bool on, Action<bool> set)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Margin = new Thickness(0, 4, 0, 2),
            Style = Application.Current?.TryFindResource("ToggleCheck") as Style,
        };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private static ComboBox Combo() => new()
    {
        Height = 26,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Style = Application.Current?.TryFindResource("DarkComboBox") as Style,
    };
}
