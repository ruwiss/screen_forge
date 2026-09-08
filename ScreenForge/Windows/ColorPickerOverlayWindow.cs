using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FontFamily = System.Windows.Media.FontFamily;

namespace ScreenForge.Windows;

/// <summary>
/// Sistem tepsisinden açılan bağımsız renk seçici.
/// Faz 1: tam ekran eyedropper overlay (<see cref="EyedropperOverlay"/>).
/// Faz 2: sağ altta renk değerleri paneli (HEX/RGB/HSL, her biri kopyalanabilir).
/// </summary>
public sealed class ColorPickerOverlayWindow
{
    private static ColorPickerOverlayWindow? _open;

    private Window? _panel;
    private Border? _swatch;
    private TextBlock? _hexLabel;
    private TextBlock? _hexVal;
    private Button? _hexCopyBtn;
    private UIElement? _copyIcon;
    private UIElement? _checkIcon;
    private TextBlock? _rgbVal;
    private TextBlock? _hslVal;
    private string _hex = "";
    private string _rgb = "";
    private string _hsl = "";
    private DispatcherTimer? _copiedTimer;

    public void Show()
    {
        _open?._panel?.Close();
        _open = this;
        EyedropperOverlay.Show(col => ApplyPicked(col, createPanel: true));
    }

    private void ApplyPicked(Color col, bool createPanel)
    {
        _hex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        Clipboard.SetText(_hex);
        var (hDeg, sPct, lPct) = RgbToHsl(col);
        _rgb = $"rgb({col.R}, {col.G}, {col.B})";
        _hsl = $"hsl({hDeg}, {sPct}%, {lPct}%)";

        if (_panel == null || createPanel)
        {
            ShowResultPanel(col);
            FlashCopied();
            return;
        }

        if (_swatch != null)
            _swatch.Background = new SolidColorBrush(col);
        if (_hexLabel != null)
            _hexLabel.Text = _hex.ToUpperInvariant();
        if (_hexVal != null)
            _hexVal.Text = _hex;
        if (_rgbVal != null)
            _rgbVal.Text = _rgb;
        if (_hslVal != null)
            _hslVal.Text = _hsl;
        FlashCopied();
        if (_panel != null)
        {
            _panel.Visibility = Visibility.Visible;
            _panel.Show();
        }
    }

    private void FlashCopied()
    {
        if (_hexCopyBtn == null || _copyIcon == null || _checkIcon == null) return;
        FlashButtonIcon(_hexCopyBtn, _copyIcon, _checkIcon, ref _copiedTimer);
    }

    private static void FlashButtonIcon(Button btn, UIElement copyIcon, UIElement checkIcon, ref DispatcherTimer? timer)
    {
        timer?.Stop();
        btn.Content = checkIcon;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        var captured = timer;
        timer.Tick += (_, _) =>
        {
            captured.Stop();
            btn.Content = copyIcon;
        };
        timer.Start();
    }

    private void ShowResultPanel(Color col)
    {
        _copiedTimer?.Stop();
        _copiedTimer = null;

        _swatch = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(col),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        _hexLabel = new TextBlock
        {
            Text = _hex.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28, Height = 28,
            FontSize = 13, FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xBE, 0x3A, 0x3A)),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeBtn.Click += (_, _) => _panel?.Close();

        var eyedropperBtn = new Button
        {
            Content = StrokeIcon("IconEyedropper", Brushes.White, 14),
            Width = 28, Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x36, 0x48)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x66)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Tekrar renk seç",
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        eyedropperBtn.Click += (_, _) => StartRepick();

        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // swatch
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // hex
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // flex
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // eyedropper
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // close
        Grid.SetColumn(_swatch, 0);
        Grid.SetColumn(_hexLabel, 1);
        Grid.SetColumn(eyedropperBtn, 3);
        Grid.SetColumn(closeBtn, 4);
        titleBar.Children.Add(_swatch);
        titleBar.Children.Add(_hexLabel);
        titleBar.Children.Add(eyedropperBtn);
        titleBar.Children.Add(closeBtn);

        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        rows.Children.Add(MakeHexRow());
        rows.Children.Add(MakeColorRow("RGB", () => _rgb, val => _rgbVal = val));
        rows.Children.Add(MakeColorRow("HSL", () => _hsl, val => _hslVal = val));

        var content = new StackPanel { Margin = new Thickness(12, 10, 10, 10) };
        content.Children.Add(titleBar);
        content.Children.Add(rows);

        var panelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x44, 0x5A)),
            BorderThickness = new Thickness(1, 1, 0, 0),
            CornerRadius = new CornerRadius(10, 0, 0, 0),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };

        var wa = SystemParameters.WorkArea;
        _panel = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 220,
            Left = wa.Right - 240,
            Top = wa.Bottom - 130,
            ResizeMode = ResizeMode.NoResize,
            Content = panelBorder,
        };

        _panel.Loaded += (_, _) =>
        {
            try
            {
                var pt = _panel.PointToScreen(new Point(0, 0));
                ChromeScale.Apply(panelBorder, ChromeScale.ForScreenPoint(_panel, (int)pt.X, (int)pt.Y));
                _panel.UpdateLayout();
            }
            catch { /* ölçek başarısızsa 1× kalır */ }

            var w = SystemParameters.WorkArea;
            _panel.Left = w.Right - _panel.ActualWidth;
            _panel.Top = w.Bottom - _panel.ActualHeight;
        };

        _panel.KeyDown += (_, e) => { if (e.Key == Key.Escape) _panel.Close(); };
        _panel.Closed += (_, _) =>
        {
            _copiedTimer?.Stop();
            _copiedTimer = null;
            if (_open == this) _open = null;
        };
        _panel.Show();
    }

    private void StartRepick()
    {
        if (_panel == null) return;
        _panel.Hide();
        EyedropperOverlay.Show(
            onPicked: col =>
            {
                ApplyPicked(col, createPanel: false);
                _panel!.Show();
            },
            onHover: null,
            onCancel: () => _panel?.Show());
    }

    private UIElement MakeHexRow()
    {
        _copyIcon = StrokeIcon("IconCopy", new SolidColorBrush(Color.FromRgb(0x88, 0x96, 0xAA)), 14);
        _checkIcon = StrokeIcon("IconCheck", new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), 14);
        _hexCopyBtn = MakeCopyButton(_copyIcon);
        _hexCopyBtn.Click += (_, _) =>
        {
            Clipboard.SetText(_hex);
            FlashCopied();
        };
        return BuildRow("HEX", _hex, _hexCopyBtn, val => _hexVal = val);
    }

    private UIElement MakeColorRow(string label, Func<string> copyValue, Action<TextBlock> bindVal)
    {
        var copyIcon = StrokeIcon("IconCopy", new SolidColorBrush(Color.FromRgb(0x88, 0x96, 0xAA)), 14);
        var checkIcon = StrokeIcon("IconCheck", new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), 14);
        var copyBtn = MakeCopyButton(copyIcon);
        DispatcherTimer? timer = null;
        copyBtn.Click += (_, _) =>
        {
            Clipboard.SetText(copyValue());
            FlashButtonIcon(copyBtn, copyIcon, checkIcon, ref timer);
        };
        return BuildRow(label, copyValue(), copyBtn, bindVal);
    }

    private static Button MakeCopyButton(UIElement content) => new()
    {
        Content = content,
        Width = 22, Height = 22,
        Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x36, 0x48)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x66)),
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand,
        Padding = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Grid BuildRow(string label, string initial, Button copyBtn, Action<TextBlock> bindVal)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x78, 0x90)),
            FontSize = 10, FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lbl, 0);

        var val = new TextBlock
        {
            Text = initial,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE4)),
            FontSize = 11, FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(val, 1);
        bindVal(val);

        Grid.SetColumn(copyBtn, 2);
        row.Children.Add(lbl);
        row.Children.Add(val);
        row.Children.Add(copyBtn);
        return row;
    }

    private static UIElement StrokeIcon(string key, Brush stroke, double size)
    {
        return new Path
        {
            Data = Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty,
            Stroke = stroke,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
    }

    private static (int h, int s, int l) RgbToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double s = 0, h = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
            else if (max == g) h = ((b - r) / d + 2) / 6.0;
            else h = ((r - g) / d + 4) / 6.0;
        }
        return ((int)Math.Round(h * 360), (int)Math.Round(s * 100), (int)Math.Round(l * 100));
    }
}
