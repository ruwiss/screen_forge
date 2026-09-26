using System.Runtime.InteropServices;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleHitHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkEscape = 0x1B;

    private readonly HookProc _mouseProc;
    private readonly HookProc _keyProc;
    private readonly Func<int, int, bool> _inside;
    private readonly Action _doubleClick;
    private readonly Action _clickOutside;
    private readonly Action _escape;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private bool _edit;
    private long _lastDown;
    private int _lastX;
    private int _lastY;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHook
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    public SubtitleHitHook(Func<int, int, bool> inside, Action doubleClick, Action clickOutside, Action escape)
    {
        _inside = inside;
        _doubleClick = doubleClick;
        _clickOutside = clickOutside;
        _escape = escape;
        _mouseProc = MouseCallback;
        _keyProc = KeyCallback;
    }

    public void SetLive(bool live)
    {
        if (live)
            EnsureMouse();
        else if (!_edit)
            RemoveMouse();
    }

    public void SetEdit(bool edit)
    {
        _edit = edit;
        if (edit)
        {
            EnsureMouse();
            EnsureKeys();
        }
        else
        {
            RemoveKeys();
        }
    }

    private void EnsureMouse()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
    }

    private void EnsureKeys()
    {
        if (_keyHook != IntPtr.Zero) return;
        _keyHook = SetWindowsHookEx(WhKeyboardLl, _keyProc, GetModuleHandle(null), 0);
    }

    private void RemoveMouse()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private void RemoveKeys()
    {
        if (_keyHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_keyHook);
        _keyHook = IntPtr.Zero;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        if (msg is not (WmLButtonDown or WmLButtonDblClk))
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<MsLlHook>(lParam);
        bool inside = false;
        try { inside = _inside(data.Point.X, data.Point.Y); } catch { /* ayar okunamadı */ }

        if (_edit)
        {
            if (!inside && msg == WmLButtonDown)
                _clickOutside();
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        if (!inside)
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        if (msg == WmLButtonDblClk)
        {
            _doubleClick();
            return 1;
        }

        long now = Environment.TickCount64;
        bool repeat = now - _lastDown <= GetDoubleClickTime()
            && Math.Abs(data.Point.X - _lastX) <= 4
            && Math.Abs(data.Point.Y - _lastY) <= 4;
        _lastDown = now;
        _lastX = data.Point.X;
        _lastY = data.Point.Y;
        if (!repeat)
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        _doubleClick();
        return 1;
    }

    private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _edit && wParam.ToInt32() is WmKeyDown or WmSysKeyDown)
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == VkEscape)
            {
                _escape();
                return 1;
            }
        }

        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _edit = false;
        RemoveMouse();
        RemoveKeys();
    }
}
