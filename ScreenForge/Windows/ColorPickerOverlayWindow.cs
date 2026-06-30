using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Image = System.Windows.Controls.Image;
using FontFamily = System.Windows.Media.FontFamily;

namespace ScreenForge.Windows;

/// <summary>
/// Sistem tepsisinden açılan bağımsız renk seçici.
/// Faz 1: tam ekran eyedropper overlay (büyüteçli).
/// Faz 2: sağ altta renk değerleri paneli (HEX/RGB/HSL, her biri kopyalanabilir).
/// </summary>
public sealed class ColorPickerOverlayWindow
{
    // P/Invoke — piksel örnekleme
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hDC, int x, int y);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDst, int xDst, int yDst, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    public void Show()
    {
        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Topmost = true,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Cursor = Cursors.Cross,
            ShowInTaskbar = false,
        };

        const int grid = 9;
        const double loupe = 90;
        double cell = loupe / grid;

        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(grid, grid, 96, 96, PixelFormats.Bgr32, null);
        var img = new Image { Width = loupe, Height = loupe, Source = bmp, IsHitTestVisible = false };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        var centerBox = new System.Windows.Shapes.Rectangle
        {
            Width = cell + 2, Height = cell + 2,
            Stroke = Brushes.White, StrokeThickness = 1.5,
            Fill = Brushes.Transparent, IsHitTestVisible = false,
        };
        Canvas.SetLeft(centerBox, (grid / 2) * cell - 1);
        Canvas.SetTop(centerBox, (grid / 2) * cell - 1);

        var magCanvas = new Canvas { Width = loupe, Height = loupe, IsHitTestVisible = false };
        magCanvas.Children.Add(img);
        magCanvas.Children.Add(centerBox);

        var hexLabel = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 10,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        var hexBg = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
            Child = hexLabel, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var loupeCircle = new Border
        {
            Width = loupe, Height = loupe,
            CornerRadius = new CornerRadius(loupe / 2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            ClipToBounds = true,
            Child = magCanvas, IsHitTestVisible = false,
        };
        loupeCircle.Clip = new EllipseGeometry(new Point(loupe / 2, loupe / 2), loupe / 2, loupe / 2);

        var loupePanel = new StackPanel { IsHitTestVisible = false };
        loupePanel.Children.Add(loupeCircle);
        loupePanel.Children.Add(hexBg);
        loupePanel.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black };

        var rootCanvas = new Canvas { IsHitTestVisible = false };
        rootCanvas.Children.Add(loupePanel);
        overlay.Content = rootCanvas;

        var capBuf = new byte[grid * grid * 4];
        var pixBuf = new byte[grid * grid * 4];
        int half = grid / 2;

        overlay.MouseMove += (_, e) =>
        {
            var screenPt = overlay.PointToScreen(e.GetPosition(overlay));
            int sx = (int)screenPt.X, sy = (int)screenPt.Y;

            IntPtr hScr = GetDC(IntPtr.Zero);
            IntPtr hMem = CreateCompatibleDC(hScr);
            IntPtr hBmp = CreateCompatibleBitmap(hScr, grid, grid);
            IntPtr hOld = SelectObject(hMem, hBmp);
            BitBlt(hMem, 0, 0, grid, grid, hScr, sx - half, sy - half, SRCCOPY);
            SelectObject(hMem, hOld);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = grid;
            bmi.bmiHeader.biHeight = -grid;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            GetDIBits(hMem, hBmp, 0, (uint)grid, capBuf, ref bmi, 0);
            DeleteObject(hBmp);
            DeleteDC(hMem);
            ReleaseDC(IntPtr.Zero, hScr);

            Buffer.BlockCopy(capBuf, 0, pixBuf, 0, pixBuf.Length);
            bmp.WritePixels(new Int32Rect(0, 0, grid, grid), pixBuf, grid * 4, 0);

            int ci = (half * grid + half) * 4;
            var col = Color.FromRgb(capBuf[ci + 2], capBuf[ci + 1], capBuf[ci]);
            hexLabel.Text = $"#{col.R:X2}{col.G:X2}{col.B:X2}";

            var lp = e.GetPosition(overlay);
            double offX = 20, offY = -20;
            double px = lp.X + offX;
            double py = lp.Y + offY - loupe;
            if (px + loupe > overlay.Width - 10) px = lp.X - offX - loupe;
            if (py < 10) py = lp.Y + offY + 20;
            Canvas.SetLeft(loupePanel, px);
            Canvas.SetTop(loupePanel, py);
        };

        overlay.MouseLeftButtonDown += (_, e) =>
        {
            var screenPt = overlay.PointToScreen(e.GetPosition(overlay));
            var col = SampleScreen((int)screenPt.X, (int)screenPt.Y);
            string hex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            Clipboard.SetText(hex);
            overlay.Close();
            ShowResultPanel(col, hex);
        };

        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) overlay.Close(); };
        overlay.Show();
        overlay.Focus();
    }

    private static Color SampleScreen(int x, int y)
    {
        IntPtr hDC = GetDC(IntPtr.Zero);
        try
        {
            uint pixel = GetPixel(hDC, x, y);
            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        finally { ReleaseDC(IntPtr.Zero, hDC); }
    }

    private static void ShowResultPanel(Color col, string hex)
    {
        var (hDeg, sPct, lPct) = RgbToHsl(col);
        string rgb = $"rgb({col.R}, {col.G}, {col.B})";
        string hsl = $"hsl({hDeg}, {sPct}%, {lPct}%)";

        Window? panel = null;

        // Başlık satırı: küçük swatch + HEX büyük + sağda X
        var swatch = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(col),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var hexLabel = new TextBlock
        {
            Text = hex.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hexCopyFeedback = new TextBlock
        {
            Text = "✓",
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            FontSize = 13, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        Window? panelRef = null;

        // Kapatma butonu — büyük ve net
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
        closeBtn.Click += (_, _) => panelRef?.Close();

        // HEX satırı: swatch + hexLabel + feedback + [esnek] + closeBtn
        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // swatch
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // hex
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // feedback
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // flex
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // close
        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(hexLabel, 1);
        Grid.SetColumn(hexCopyFeedback, 2);
        Grid.SetColumn(closeBtn, 4);
        titleBar.Children.Add(swatch);
        titleBar.Children.Add(hexLabel);
        titleBar.Children.Add(hexCopyFeedback);
        titleBar.Children.Add(closeBtn);

        // Renk satırları
        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        rows.Children.Add(MakeColorRow("HEX", hex, hexCopyFeedback));
        rows.Children.Add(MakeColorRow("RGB", rgb));
        rows.Children.Add(MakeColorRow("HSL", hsl));

        var content = new StackPanel { Margin = new Thickness(12, 10, 10, 10) };
        content.Children.Add(titleBar);
        content.Children.Add(rows);

        var panelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x44, 0x5A)),
            BorderThickness = new Thickness(1, 1, 0, 0),
            // Sadece sol üst köşe yuvarlak; sağ + alt köşeler ekrana yapışık
            CornerRadius = new CornerRadius(10, 0, 0, 0),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };

        var wa = SystemParameters.WorkArea;
        panel = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 220,
            // Başlangıç — Loaded'da gerçek boyutla güncellenir
            Left = wa.Right - 240,
            Top = wa.Bottom - 130,
            ResizeMode = ResizeMode.NoResize,
            Content = panelBorder,
        };
        panelRef = panel;

        // Gerçek boyut belli olunca tam sağa-alta yapıştır
        panel.Loaded += (_, _) =>
        {
            var w = SystemParameters.WorkArea;
            panel!.Left = w.Right - panel.ActualWidth;
            panel.Top = w.Bottom - panel.ActualHeight;
        };

        panel.KeyDown += (_, e) => { if (e.Key == Key.Escape) panel.Close(); };
        panel.Show();
    }

    // feedback = HEX satırına ait ✓ göstergesi (sadece HEX satırı için)
    private static UIElement MakeColorRow(string label, string copyValue, TextBlock? feedback = null)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); // etiket
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // değer
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // kopyala

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
            Text = copyValue,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE4)),
            FontSize = 11, FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(val, 1);

        var copyBtn = new Button
        {
            Content = MakeCopyIcon(),
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

        // Kopyalama feedback: ikon geçici ✓'ye dönüşür
        var feedbackRef = feedback; // null ise kendi satırına ait anonim feedback
        TextBlock? ownFeedback = null;
        if (feedbackRef == null)
        {
            ownFeedback = new TextBlock
            {
                Text = "✓",
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                FontSize = 11, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
        }

        copyBtn.Click += (_, _) =>
        {
            Clipboard.SetText(copyValue);
            var fb = feedbackRef ?? ownFeedback!;
            fb.Visibility = Visibility.Visible;
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1.2) };
            timer.Tick += (_, _) => { fb.Visibility = Visibility.Collapsed; timer.Stop(); };
            timer.Start();
        };
        Grid.SetColumn(copyBtn, 2);

        row.Children.Add(lbl);
        row.Children.Add(val);
        row.Children.Add(copyBtn);
        if (ownFeedback != null)
        {
            // ✓ değer'in üstüne overlay gibi koy (Grid'de zaten aynı column'da)
            ownFeedback.HorizontalAlignment = HorizontalAlignment.Right;
            ownFeedback.Margin = new Thickness(0, 0, 26, 0);
            Grid.SetColumn(ownFeedback, 1);
            row.Children.Add(ownFeedback);
        }
        return row;
    }

    private static UIElement MakeCopyIcon()
    {
        // SVG path'leri 24×24 viewBox, scale → 14×14
        const string frontPath = "M6 11C6 8.17 6 6.76 6.88 5.88C7.76 5 9.17 5 12 5H15C17.83 5 19.24 5 20.12 5.88C21 6.76 21 8.17 21 11V16C21 18.83 21 20.24 20.12 21.12C19.24 22 17.83 22 15 22H12C9.17 22 7.76 22 6.88 21.12C6 20.24 6 18.83 6 16Z";
        const string backPath  = "M6 19C4.34 19 3 17.66 3 16V10C3 6.23 3 4.34 4.17 3.17C5.34 2 7.23 2 11 2H15C16.66 2 18 3.34 18 5";
        var iconColor = new SolidColorBrush(Color.FromRgb(0x88, 0x96, 0xAA));
        var c = new System.Windows.Controls.Canvas { Width = 14, Height = 14, IsHitTestVisible = false };
        var scale = new System.Windows.Media.ScaleTransform(14.0 / 24.0, 14.0 / 24.0);
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(backPath), Stroke = iconColor, StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent, Opacity = 0.55,
            RenderTransform = scale,
        });
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(frontPath), Stroke = iconColor, StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            RenderTransform = scale,
        });
        return c;
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
