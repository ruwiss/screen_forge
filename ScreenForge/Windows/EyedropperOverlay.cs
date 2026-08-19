using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// Tam ekran damlalık overlay'i — hem tepsi renk seçicisi hem de
/// <see cref="ColorPickerPopup"/> tarafından kullanılır.
/// Büyüteç imlecin yerine geçer: sistem imleci gizlenir, loupe fare
/// konumuna ortalanır ve merkez piksel kutusu nişangâh görevi görür.
/// Overlay WDA_EXCLUDEFROMCAPTURE ile ekran yakalamadan gizlendiği için
/// loupe kendi çizimini örneklemez. Bu API (Win10 2004+) başarısız olursa
/// eski davranışa (imleç artı + loupe sağ üstte) düşülür.
/// </summary>
internal static class EyedropperOverlay
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hDC, int x, int y);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDst, int xDst, int yDst, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    private const int Grid = 13;            // örneklenen piksel karesi (tek sayı olmalı)
    private const double LoupeSize = 104;   // Grid'e tam bölünmeli → cell = 8

    /// <param name="onPicked">Sol tık ile renk seçildiğinde çağrılır (overlay kapanır).</param>
    /// <param name="onHover">Fare gezdikçe anlık renk — canlı önizleme isteyen çağıranlar için.</param>
    public static void Show(Action<Color> onPicked, Action<Color>? onHover = null)
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
            ShowInTaskbar = false,
        };

        const double cell = LoupeSize / Grid;
        const int half = Grid / 2;

        var bmp = new WriteableBitmap(Grid, Grid, 96, 96, PixelFormats.Bgr32, null);
        var img = new Image { Width = LoupeSize, Height = LoupeSize, Source = bmp, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        var magCanvas = new Canvas { Width = LoupeSize, Height = LoupeSize, IsHitTestVisible = false };
        magCanvas.Children.Add(img);
        // Merkez piksel nişangâhı: koyu dış hat + beyaz iç hat, her zemin üzerinde okunur
        magCanvas.Children.Add(CenterMarker(cell + 4, Color.FromArgb(160, 0, 0, 0), 1.0, half * cell - 2));
        magCanvas.Children.Add(CenterMarker(cell + 2, Colors.White, 1.5, half * cell - 1));

        var loupeCircle = new Border
        {
            Width = LoupeSize, Height = LoupeSize,
            CornerRadius = new CornerRadius(LoupeSize / 2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            ClipToBounds = true,
            Child = magCanvas,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };
        loupeCircle.Clip = new EllipseGeometry(new Point(LoupeSize / 2, LoupeSize / 2), LoupeSize / 2, LoupeSize / 2);

        var hexLabel = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 10,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        var hexBg = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Child = hexLabel,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.5, Color = Colors.Black },
        };
        // Sabit genişlikli kap: etiket metni değişse de loupe ile aynı eksende kalır
        var hexHolder = new Grid { Width = LoupeSize, IsHitTestVisible = false };
        hexHolder.Children.Add(hexBg);

        var rootCanvas = new Canvas { IsHitTestVisible = false };
        rootCanvas.Children.Add(loupeCircle);
        rootCanvas.Children.Add(hexHolder);
        overlay.Content = rootCanvas;

        bool loupeIsCursor = false;
        overlay.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(overlay).Handle;
            loupeIsCursor = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            overlay.Cursor = loupeIsCursor ? Cursors.None : Cursors.Cross;
        };

        var capBuf = new byte[Grid * Grid * 4];

        void UpdateLoupe(Point lp)
        {
            var screenPt = overlay.PointToScreen(lp);
            int sx = (int)screenPt.X, sy = (int)screenPt.Y;

            IntPtr hScr = GetDC(IntPtr.Zero);
            IntPtr hMem = CreateCompatibleDC(hScr);
            IntPtr hBmp = CreateCompatibleBitmap(hScr, Grid, Grid);
            IntPtr hOld = SelectObject(hMem, hBmp);
            BitBlt(hMem, 0, 0, Grid, Grid, hScr, sx - half, sy - half, SRCCOPY);
            SelectObject(hMem, hOld);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = Grid;
            bmi.bmiHeader.biHeight = -Grid;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            GetDIBits(hMem, hBmp, 0, (uint)Grid, capBuf, ref bmi, 0);
            DeleteObject(hBmp);
            DeleteDC(hMem);
            ReleaseDC(IntPtr.Zero, hScr);

            bmp.WritePixels(new Int32Rect(0, 0, Grid, Grid), capBuf, Grid * 4, 0);

            int ci = (half * Grid + half) * 4;
            var col = Color.FromRgb(capBuf[ci + 2], capBuf[ci + 1], capBuf[ci]);
            hexLabel.Text = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            onHover?.Invoke(col);

            double cx, cy;
            if (loupeIsCursor)
            {
                cx = lp.X - LoupeSize / 2;
                cy = lp.Y - LoupeSize / 2;
            }
            else
            {
                // Yedek yol: imleç artı kalır, loupe sağ üstte — ekran kenarında ters tarafa geçer
                const double offX = 20, offY = -20;
                cx = lp.X + offX;
                cy = lp.Y + offY - LoupeSize;
                if (cx + LoupeSize > overlay.Width - 10) cx = lp.X - offX - LoupeSize;
                if (cy < 10) cy = lp.Y + offY + 20;
            }

            Canvas.SetLeft(loupeCircle, cx);
            Canvas.SetTop(loupeCircle, cy);

            // Hex etiketi loupe'nin altında; ekranın dibindeyse üstüne alınır
            double hexTop = cy + LoupeSize + 4;
            if (hexTop + 22 > overlay.Height) hexTop = cy - 22;
            Canvas.SetLeft(hexHolder, cx);
            Canvas.SetTop(hexHolder, hexTop);
        }

        overlay.MouseMove += (_, e) => UpdateLoupe(e.GetPosition(overlay));

        overlay.MouseLeftButtonDown += (_, e) =>
        {
            var screenPt = overlay.PointToScreen(e.GetPosition(overlay));
            var col = SampleScreen((int)screenPt.X, (int)screenPt.Y);
            overlay.Close();
            onPicked(col);
        };

        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) overlay.Close(); };

        overlay.Show();
        overlay.Focus();

        // İlk MouseMove'u beklemeden doğru yere yerleş — köşede boş loupe görünmesin
        if (GetCursorPos(out var cur))
            UpdateLoupe(overlay.PointFromScreen(new Point(cur.X, cur.Y)));
    }

    private static WpfRectangle CenterMarker(double size, Color stroke, double thickness, double offset)
    {
        var r = new WpfRectangle
        {
            Width = size, Height = size,
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = thickness,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(r, offset);
        Canvas.SetTop(r, offset);
        return r;
    }

    public static Color SampleScreen(int x, int y)
    {
        IntPtr hDC = GetDC(IntPtr.Zero);
        try
        {
            uint pixel = GetPixel(hDC, x, y); // COLORREF: 0x00BBGGRR
            return Color.FromRgb((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
        }
        finally { ReleaseDC(IntPtr.Zero, hDC); }
    }
}
