using System.Runtime.InteropServices;
using SkiaSharp;

namespace ScreenForge.Presenter;

public sealed class PresenterMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkEscape = 0x1B;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    private readonly HookProc _mouseProc;
    private readonly HookProc _keyProc;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private bool _eat;
    private bool _session;
    private bool _down;
    private int _ateVk;

    public event Action<SKPoint>? Pressed;
    public event Action<SKPoint>? Moved;
    public event Action<SKPoint>? Released;
    public event Action? Escape;
    public Func<int, bool>? TryEatKey { get; set; }
    public Action<int>? AteKeyUp { get; set; }

    public PresenterMouseHook()
    {
        _mouseProc = MouseCallback;
        _keyProc = KeyCallback;
    }

    public bool Eat
    {
        get => _eat;
        set
        {
            _eat = value;
            if (value) EnsureMouse();
            else if (!_down) RemoveMouse();
        }
    }

    public bool SessionActive
    {
        get => _session;
        set
        {
            _session = value;
            if (value) EnsureKeys();
            else RemoveKeys();
        }
    }

    private void EnsureMouse()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
    }

    private void RemoveMouse()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private void EnsureKeys()
    {
        if (_keyHook != IntPtr.Zero) return;
        _keyHook = SetWindowsHookEx(WhKeyboardLl, _keyProc, GetModuleHandle(null), 0);
    }

    private void RemoveKeys()
    {
        if (_keyHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_keyHook);
        _keyHook = IntPtr.Zero;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _eat)
        {
            int msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var p = new SKPoint(data.Point.X, data.Point.Y);
            switch (msg)
            {
                case WmLButtonDown:
                case WmLButtonDblClk:
                    _down = true;
                    Pressed?.Invoke(p);
                    return 1;
                case WmLButtonUp:
                    _down = false;
                    Released?.Invoke(p);
                    return 1;
                case WmMouseMove:
                    Moved?.Invoke(p);
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _session)
        {
            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam);
            if (msg is WmKeyUp or WmSysKeyUp)
            {
                if (vk == _ateVk)
                {
                    _ateVk = 0;
                    AteKeyUp?.Invoke(vk);
                    return 1;
                }
            }
            else if (msg is WmKeyDown or WmSysKeyDown)
            {
                if (vk == VkEscape)
                {
                    Escape?.Invoke();
                    return 1;
                }
                if (vk == _ateVk)
                    return 1;
                if (TryEatKey?.Invoke(vk) == true)
                {
                    _ateVk = vk;
                    return 1;
                }
            }
        }

        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _eat = false;
        _session = false;
        _down = false;
        RemoveMouse();
        RemoveKeys();
    }
}
