using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using ScreenForge.Capture;
using Image = System.Windows.Controls.Image;

namespace ScreenForge.Presenter;

public sealed class PresenterOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpFramechanged = 0x0020;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    private const uint WdaExcludeFromCapture = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private readonly Image _image;
    private readonly Action<SKCanvas, int, int> _paint;
    private readonly Rectangle _virtual = ScreenCapture.VirtualScreenBounds;
    private WriteableBitmap? _bitmap;
    private bool _hooked;
    private bool _dirty = true;

    public event Action? FrameTick;

    public PresenterOverlayWindow(Action<SKCanvas, int, int> paint)
    {
        _paint = paint;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _image = new Image
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
        };
        Content = _image;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // 1px kısa: Windows tam ekran sayıp görev çubuğunu gizlemesin.
            MoveWindow(hwnd, _virtual.X, _virtual.Y, _virtual.Width, Math.Max(1, _virtual.Height - 1), true);
            int ex = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, ex | WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNozorder | SwpNoactivate | SwpFramechanged);
            SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
            EnsureBitmap();
        };

        Loaded += (_, _) =>
        {
            if (_hooked) return;
            CompositionTarget.Rendering += OnRenderTick;
            _hooked = true;
        };
        Closed += (_, _) =>
        {
            if (!_hooked) return;
            CompositionTarget.Rendering -= OnRenderTick;
            _hooked = false;
        };
    }

    public SKPoint CursorCanvas()
    {
        GetCursorPos(out var p);
        return new SKPoint(p.X - _virtual.X, p.Y - _virtual.Y);
    }

    public SKPoint ToCanvas(SKPoint screen) => new(screen.X - _virtual.X, screen.Y - _virtual.Y);

    public SKRect CanvasBounds => new(0, 0, _virtual.Width, _virtual.Height);

    public void Redraw() => _dirty = true;

    private void OnRenderTick(object? sender, EventArgs e)
    {
        FrameTick?.Invoke();
        if (!_dirty) return;
        _dirty = false;
        Flush();
    }

    private void EnsureBitmap()
    {
        int w = Math.Max(1, _virtual.Width);
        int h = Math.Max(1, _virtual.Height);
        if (_bitmap != null && _bitmap.PixelWidth == w && _bitmap.PixelHeight == h)
            return;
        _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        _image.Source = _bitmap;
    }

    private void Flush()
    {
        EnsureBitmap();
        if (_bitmap == null) return;
        _bitmap.Lock();
        try
        {
            var info = new SKImageInfo(_bitmap.PixelWidth, _bitmap.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            _paint(surface.Canvas, info.Width, info.Height);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }
}
