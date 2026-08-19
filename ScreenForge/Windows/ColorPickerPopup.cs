using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// Tema uyumlu, bağımlılıksız HSV özel renk seçici (kod ile kurulu Popup).
/// SV karesi + Hue şeridi + hex girişi + damlalık. Renk seçilince OnPicked geri çağırır.
/// </summary>
public sealed class ColorPickerPopup
{
    private readonly Popup _popup;
    private readonly Action<System.Windows.Media.Color> _onPicked;

    private double _h;          // 0..360
    private double _s = 1;      // 0..1
    private double _v = 1;      // 0..1

    private readonly Canvas _svCanvas;
    private readonly Border _svThumb;
    private readonly WpfRectangle _svColorLayer;
    private readonly Canvas _hueCanvas;
    private readonly Border _hueThumb;
    private readonly Border _preview;
    private readonly TextBox _hexBox;
    private bool _updating;

    public ColorPickerPopup(UIElement placementTarget, System.Windows.Media.Color initial, Action<System.Windows.Media.Color> onPicked)
    {
        _onPicked = onPicked;
        (_h, _s, _v) = RgbToHsv(initial);

        var surface = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x24, 0x30));
        var border = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x42, 0x54));

        // SV karesi: alt katman = hue rengi, üzerine beyaz→şeffaf (yatay) ve şeffaf→siyah (dikey).
        _svColorLayer = new WpfRectangle { Width = 200, Height = 150 };
        var whiteLayer = new WpfRectangle
        {
            Width = 200, Height = 150,
            Fill = new LinearGradientBrush(Colors.White, System.Windows.Media.Color.FromArgb(0, 255, 255, 255), new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5)),
        };
        var blackLayer = new WpfRectangle
        {
            Width = 200, Height = 150,
            Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), Colors.Black, new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1)),
        };
        _svThumb = new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };
        _svThumb.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 };
        _svCanvas = new Canvas { Width = 200, Height = 150, ClipToBounds = true, Cursor = Cursors.Cross };
        _svCanvas.Children.Add(_svColorLayer);
        _svCanvas.Children.Add(whiteLayer);
        _svCanvas.Children.Add(blackLayer);
        _svCanvas.Children.Add(_svThumb);
        _svCanvas.MouseLeftButtonDown += (_, e) => { _svCanvas.CaptureMouse(); UpdateSV(e.GetPosition(_svCanvas)); };
        _svCanvas.MouseMove += (_, e) => { if (_svCanvas.IsMouseCaptured) UpdateSV(e.GetPosition(_svCanvas)); };
        _svCanvas.MouseLeftButtonUp += (_, _) => _svCanvas.ReleaseMouseCapture();

        // Hue şeridi (dikey)
        var hueRect = new WpfRectangle
        {
            Width = 18, Height = 150,
            Fill = MakeHueBrush(),
        };
        _hueThumb = new Border
        {
            Width = 22, Height = 6, CornerRadius = new CornerRadius(3),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };
        _hueCanvas = new Canvas { Width = 22, Height = 150, ClipToBounds = false, Cursor = Cursors.SizeNS };
        Canvas.SetLeft(hueRect, 2);
        _hueCanvas.Children.Add(hueRect);
        _hueCanvas.Children.Add(_hueThumb);
        _hueCanvas.MouseLeftButtonDown += (_, e) => { _hueCanvas.CaptureMouse(); UpdateHue(e.GetPosition(_hueCanvas)); };
        _hueCanvas.MouseMove += (_, e) => { if (_hueCanvas.IsMouseCaptured) UpdateHue(e.GetPosition(_hueCanvas)); };
        _hueCanvas.MouseLeftButtonUp += (_, _) => _hueCanvas.ReleaseMouseCapture();

        var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
        pickRow.Children.Add(_svCanvas);
        pickRow.Children.Add(new Border { Width = 8 });
        pickRow.Children.Add(_hueCanvas);

        // Önizleme + hex + damlalık
        _preview = new Border { Width = 34, Height = 28, CornerRadius = new CornerRadius(5), BorderBrush = border, BorderThickness = new Thickness(1) };
        _hexBox = new TextBox
        {
            Width = 90, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0x2D, 0x3B)),
            Foreground = Brushes.White, BorderBrush = border, Padding = new Thickness(6, 3, 6, 3),
        };
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        _hexBox.LostFocus += (_, _) => ApplyHex();

        var eyedropperBtn = BuildEyedropperButton();
        eyedropperBtn.Click += (_, _) => StartEyedropper();

        var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bottomRow.Children.Add(_preview);
        bottomRow.Children.Add(_hexBox);
        bottomRow.Children.Add(eyedropperBtn);

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(pickRow);
        root.Children.Add(bottomRow);

        var card = new Border
        {
            Background = surface, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Child = root,
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 };

        _popup = new Popup
        {
            Child = card, PlacementTarget = placementTarget, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
        };

        SyncFromHsv();
    }

    public void Open()
    {
        _popup.IsOpen = true;
        // Popup ayrı HWND olabilir; ölçeği kartın kendi pencere DPI'sinden al (overlay DPI ile çift zoom olmasın).
        if (_popup.Child is FrameworkElement card)
        {
            try
            {
                var pt = card.PointToScreen(new Point(0, 0));
                ChromeScale.Apply(card, ChromeScale.ForScreenPoint(card, (int)pt.X, (int)pt.Y));
            }
            catch { /* ölçek başarısızsa 1× kalır */ }
        }
    }

    // ---- Damlalık ----
    private static Button BuildEyedropperButton()
    {
        // Damlalık ikonu: SVG path'lerden GeometryGroup
        var gg = new GeometryGroup();
        gg.Children.Add(Geometry.Parse("M6.34,17.73h0a6.9,6.9,0,0,1,2-4.89L14,7.23l2.86,2.86L11.23,15.7A6.92,6.92,0,0,1,6.34,17.73Z"));
        gg.Children.Add(Geometry.Parse("M17.32,10.57L13.5,6.75,18,2.29a2.69,2.69,0,0,1,1.91-.79h0a2.7,2.7,0,0,1,2.7,2.7h0a2.73,2.73,0,0,1-.79,1.91Z"));
        gg.Children.Add(Geometry.Parse("M12.07,5.32 L18.75,12"));
        gg.Children.Add(Geometry.Parse("M6.34,21.55a1,1,0,0,1-1.91,0,6.27,6.27,0,0,1,1-1.91A6.29,6.29,0,0,1,6.34,21.55Z"));
        var iconPath = new Path
        {
            Data = gg,
            Stroke = Brushes.White,
            Fill = Brushes.Transparent,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 14,
            Height = 14,
            IsHitTestVisible = false,
        };

        var btn = new Button
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            Content = iconPath,
            ToolTip = "Ekrandan renk seç",
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0x2D, 0x3B)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x42, 0x54)),
            Cursor = Cursors.Hand,
        };

        // Stil: CornerRadius + hover
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentPresenterFactory);
        template.VisualTree = borderFactory;

        // Hover trigger
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x3D, 0x52)), "bd"));
        // (setter name requires named element — use simpler style approach instead)

        btn.Template = template;

        // Hover rengi için EventHandler
        btn.MouseEnter += (s, _) =>
        {
            if (s is Button b)
                b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x3D, 0x52));
        };
        btn.MouseLeave += (s, _) =>
        {
            if (s is Button b)
                b.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0x2D, 0x3B));
        };

        return btn;
    }

    private void StartEyedropper()
    {
        _popup.IsOpen = false;

        EyedropperOverlay.Show(
            onPicked: col => { UpdateFromColor(col); Commit(); },
            onHover: UpdateFromColor);
    }

    private void UpdateFromColor(System.Windows.Media.Color col)
    {
        (_h, _s, _v) = RgbToHsv(col);
        SyncFromHsv();
    }

    // ---- Etkileşim ----
    private void UpdateSV(System.Windows.Point pos)
    {
        _s = Math.Clamp(pos.X / _svCanvas.Width, 0, 1);
        _v = Math.Clamp(1 - pos.Y / _svCanvas.Height, 0, 1);
        SyncFromHsv();
        Commit();
    }

    private void UpdateHue(System.Windows.Point pos)
    {
        _h = Math.Clamp(pos.Y / _hueCanvas.Height, 0, 1) * 360;
        SyncFromHsv();
        Commit();
    }

    private void ApplyHex()
    {
        var c = ParseHex(_hexBox.Text);
        if (c.HasValue)
        {
            (_h, _s, _v) = RgbToHsv(c.Value);
            SyncFromHsv();
            Commit();
        }
    }

    private void SyncFromHsv()
    {
        _updating = true;
        var col = HsvToRgb(_h, _s, _v);
        _svColorLayer.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
        Canvas.SetLeft(_svThumb, _s * _svCanvas.Width - 6);
        Canvas.SetTop(_svThumb, (1 - _v) * _svCanvas.Height - 6);
        Canvas.SetTop(_hueThumb, _h / 360 * _hueCanvas.Height - 3);
        _preview.Background = new SolidColorBrush(col);
        _hexBox.Text = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        _updating = false;
    }

    private void Commit()
    {
        if (_updating) return;
        _onPicked(HsvToRgb(_h, _s, _v));
    }

    // ---- Renk dönüşümleri ----
    private static LinearGradientBrush MakeHueBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
        for (int i = 0; i <= 6; i++)
            b.GradientStops.Add(new GradientStop(HsvToRgb(i * 60, 1, 1), i / 6.0));
        return b;
    }

    private static System.Windows.Media.Color HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(System.Windows.Media.Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static System.Windows.Media.Color? ParseHex(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length == 6 &&
            int.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, null, out int r) &&
            int.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, null, out int g) &&
            int.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, null, out int b))
            return System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b);
        return null;
    }
}
