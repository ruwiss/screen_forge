using System.Runtime.InteropServices;

namespace ScreenForge.Record;

internal sealed class GdiFrameSource : IFrameSource
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);
    [DllImport("kernel32.dll")] private static extern bool QueryPerformanceCounter(out long value);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    private readonly System.Drawing.Rectangle _region;
    private IntPtr _screenDc, _memoryDc, _bitmap, _oldBitmap;
    private BITMAPINFO _info;
    private bool _disposed;
    private readonly byte[] _scratch;
    public GdiFrameSource(System.Drawing.Rectangle region)
    {
        _region = region;
        int w = region.Width, h = region.Height;
        _scratch = new byte[Math.Max(0, w) * Math.Max(0, h) * 4];
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        _bitmap = CreateCompatibleBitmap(_screenDc, w, h);
        _oldBitmap = SelectObject(_memoryDc, _bitmap);
        _info = new BITMAPINFO
        {
            bmiHeader =
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
    }

    public bool TryCopyBgra(Span<byte> dest, out long qpcHundredNanos)
    {
        qpcHundredNanos = 0;
        if (_disposed || dest.Length < _region.Width * _region.Height * 4)
            return false;

        if (!BitBlt(_memoryDc, 0, 0, _region.Width, _region.Height, _screenDc, _region.X, _region.Y, SRCCOPY | CAPTUREBLT))
            return false;
        if (GetDIBits(_memoryDc, _bitmap, 0, (uint)_region.Height, _scratch, ref _info, 0) == 0)
            return false;

        _scratch.AsSpan(0, dest.Length).CopyTo(dest);
        QueryPerformanceCounter(out long qpc);
        qpcHundredNanos = QpcToHundredNanos(qpc);
        return true;
    }

    private static long QpcToHundredNanos(long qpc)
    {
        QueryPerformanceFrequency(out long freq);
        return freq == 0 ? 0 : qpc * 10_000_000L / freq;
    }

    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceFrequency(out long value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_memoryDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_memoryDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        if (_screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screenDc);
        _screenDc = _memoryDc = _bitmap = _oldBitmap = IntPtr.Zero;
    }
}
