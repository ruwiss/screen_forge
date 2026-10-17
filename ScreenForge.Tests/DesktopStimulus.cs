using System.Drawing;
using System.Runtime.InteropServices;

namespace CaptureTesting;

/// <summary>WPF/Skia'dan bağımsız GDI renk penceresi; testin uyarıcısı da yakalama önbelleği kullanmaz.</summary>
internal sealed class DesktopStimulus : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref RECT rect, nint brush);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
    private delegate nint WndProc(nint hwnd, uint msg, nint wp, nint lp);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASS
    {
        public uint Style; public WndProc Proc; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT
    {
        public nint Dc; public int Erase; public RECT Rect; public int Restore, IncUpdate;
        public long Reserved1, Reserved2, Reserved3, Reserved4;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    private readonly string _className = "ScreenForgeProbe-" + Guid.NewGuid().ToString("N");
    private readonly WndProc _proc;
    private readonly nint _oldDpi;
    private Color _color;
    internal nint Handle { get; }
    internal int Left => 180;
    internal int Top => 220;

    internal DesktopStimulus()
    {
        _oldDpi = SetThreadDpiAwarenessContext(new nint(-4));
        _proc = WindowProc;
        var wc = new WNDCLASS { Proc = _proc, Name = _className };
        if (RegisterClass(ref wc) == 0) throw new InvalidOperationException("Renk penceresi sınıfı oluşturulamadı.");
        Handle = CreateWindowEx(0x88, _className, "ScreenForge CaptureProbe", 0x90000000, Left, Top, 480, 340, 0, 0, 0, 0);
        if (Handle == 0) throw new InvalidOperationException("Renk penceresi oluşturulamadı.");
    }

    internal void Present(Color color)
    {
        _color = color;
        SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, 0x13);
        InvalidateRect(Handle, 0, false);
        UpdateWindow(Handle);
        // FillRect iş parçacığının GDI komut grubunda kalmasın. DwmFlush tek
        // başına bu grubu göndermez; henüz çizilmemiş uyarıcı ölçülmemeli.
        if (!GdiFlush()) throw new InvalidOperationException("GDI renk uyarıcısını tamamlayamadı.");
        if (DwmFlush() != 0) throw new InvalidOperationException("DWM renk uyarıcısını sunamadı.");
    }

    internal Color ReadPresentedPixel()
    {
        nint dc = GetDC(0);
        try
        {
            uint pixel = GetPixel(dc, Left + 240, Top + 170);
            return Color.FromArgb((int)(pixel & 255), (int)((pixel >> 8) & 255), (int)((pixel >> 16) & 255));
        }
        finally { ReleaseDC(0, dc); }
    }

    private nint WindowProc(nint hwnd, uint msg, nint wp, nint lp)
    {
        if (msg != 0xF) return DefWindowProc(hwnd, msg, wp, lp);
        var dc = BeginPaint(hwnd, out var ps);
        nint brush = CreateSolidBrush((uint)(_color.R | _color.G << 8 | _color.B << 16));
        var rect = new RECT { Right = 480, Bottom = 340 };
        FillRect(dc, ref rect, brush);
        DeleteObject(brush);
        EndPaint(hwnd, ref ps);
        return 0;
    }

    internal static void Pump()
    {
        while (PeekMessage(out var msg, 0, 0, 0, 1)) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
    }

    public void Dispose()
    {
        DestroyWindow(Handle);
        UnregisterClass(_className, 0);
        SetThreadDpiAwarenessContext(_oldDpi);
        GC.KeepAlive(_proc);
    }
}
