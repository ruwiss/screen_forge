using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenForge.Windows;

internal static partial class DarkTitleBar
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        int value = 1;
        // 20: dark mode, 33: yuvarlak köşe (Win11)
        DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref value, sizeof(int));
    }
}
