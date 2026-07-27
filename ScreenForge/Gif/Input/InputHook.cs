using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ScreenForge.Gif.Input;

/// <summary>Düşük seviye fare olayları.</summary>
public enum MouseEventType
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    ExtraDown,
    ExtraUp,
    Wheel,
    WheelHorizontal,
    DragStart,
    DragEnd,
    DoubleClick,
}

/// <summary>Hangi fare düğmelerinin basılı olduğu.</summary>
[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    Extra1 = 8,
    Extra2 = 16,
}

/// <summary>Tek bir fare olayı (sanal ekran piksel koordinatlarında).</summary>
public readonly record struct MouseGesture(
    MouseEventType Type,
    int X,
    int Y,
    MouseButtons Buttons,
    int WheelDelta);

/// <summary>Tek bir klavye olayı.</summary>
public readonly record struct KeyGesture(Key Key, System.Windows.Input.ModifierKeys Modifiers, bool IsDown);

/// <summary>
/// Uygulama arka planda olsa bile fare ve klavye etkinliğini dinler.
/// ScreenToGif'in InputHook'undan uyarlandı: fare ve klavye <b>ayrı</b> düşük seviye
/// kancalardır, böylece biri kapalıyken diğeri maliyet doğurmaz.
/// </summary>
/// <remarks>
/// Kanca geri çağırmaları tüm sistemin girdi hattında çalışır; içeride iş yapmak
/// gecikmeye yol açar. Bu yüzden burada yalnızca hafif olay üretimi yapılır.
/// </remarks>
public sealed class InputHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_RBUTTONDBLCLK = 0x0206;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MBUTTONDBLCLK = 0x0209;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private const int XBUTTON1 = 0x0001;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>Fare hareket ettiğinde, düğmeye basıldığında veya tekerlek döndüğünde.</summary>
    public event Action<MouseGesture>? MouseActivity;

    /// <summary>Bir tuşa basıldığında veya bırakıldığında.</summary>
    public event Action<KeyGesture>? KeyActivity;

    // Delegeler alan olarak tutulmalı; aksi hâlde GC toplar ve kanca çöker.
    private HookProc? _mouseProc;
    private HookProc? _keyboardProc;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;

    private MouseButtons _buttons;
    private long _lastClickTicks;
    private int _dragStartX, _dragStartY;
    private bool _dragging;
    private bool _disposed;

    private readonly int _dragThresholdX = System.Windows.SystemParameters.MinimumHorizontalDragDistance > 0
        ? (int)System.Windows.SystemParameters.MinimumHorizontalDragDistance
        : 4;

    private readonly int _dragThresholdY = System.Windows.SystemParameters.MinimumVerticalDragDistance > 0
        ? (int)System.Windows.SystemParameters.MinimumVerticalDragDistance
        : 4;

    public bool IsMouseHooked => _mouseHook != IntPtr.Zero;
    public bool IsKeyboardHooked => _keyboardHook != IntPtr.Zero;

    /// <summary>Şu an basılı olan fare düğmeleri.</summary>
    public MouseButtons CurrentButtons => _buttons;

    /// <summary>
    /// İstenen kancaları kurar. Zaten kurulu olanlar yeniden kurulmaz.
    /// Kurulum başarısız olursa ilgili kanca sessizce devre dışı kalır — kayıt yine sürer.
    /// </summary>
    public void Start(bool mouse, bool keyboard)
    {
        var moduleHandle = GetModuleHandle(null);

        if (mouse && _mouseHook == IntPtr.Zero)
        {
            _mouseProc = MouseHookProc;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
            if (_mouseHook == IntPtr.Zero)
                _mouseProc = null;
        }

        if (keyboard && _keyboardHook == IntPtr.Zero)
        {
            _keyboardProc = KeyboardHookProc;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            if (_keyboardHook == IntPtr.Zero)
                _keyboardProc = null;
        }
    }

    public void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseProc = null;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _keyboardProc = null;
        }

        _buttons = MouseButtons.None;
        _dragging = false;
    }

    // ─── Fare ─────────────────────────────────────────────────────────────────

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || MouseActivity == null)
            return CallNextHookEx(_mouseHook, code, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int message = (int)wParam;
            int x = data.Point.X, y = data.Point.Y;
            int wheel = unchecked((short)((data.MouseData >> 16) & 0xffff));
            int xButton = (int)((data.MouseData >> 16) & 0xffff);

            switch (message)
            {
                case WM_MOUSEMOVE:
                    HandleMove(x, y);
                    break;

                case WM_LBUTTONDOWN:
                    _buttons |= MouseButtons.Left;
                    _dragStartX = x;
                    _dragStartY = y;
                    RaiseClick(MouseEventType.LeftDown, x, y);
                    break;

                case WM_LBUTTONUP:
                    if (_dragging)
                    {
                        Raise(MouseEventType.DragEnd, x, y);
                        _dragging = false;
                    }
                    _buttons &= ~MouseButtons.Left;
                    Raise(MouseEventType.LeftUp, x, y);
                    break;

                case WM_RBUTTONDOWN:
                    _buttons |= MouseButtons.Right;
                    RaiseClick(MouseEventType.RightDown, x, y);
                    break;

                case WM_RBUTTONUP:
                    _buttons &= ~MouseButtons.Right;
                    Raise(MouseEventType.RightUp, x, y);
                    break;

                case WM_MBUTTONDOWN:
                    _buttons |= MouseButtons.Middle;
                    RaiseClick(MouseEventType.MiddleDown, x, y);
                    break;

                case WM_MBUTTONUP:
                    _buttons &= ~MouseButtons.Middle;
                    Raise(MouseEventType.MiddleUp, x, y);
                    break;

                case WM_XBUTTONDOWN:
                    _buttons |= xButton == XBUTTON1 ? MouseButtons.Extra1 : MouseButtons.Extra2;
                    Raise(MouseEventType.ExtraDown, x, y);
                    break;

                case WM_XBUTTONUP:
                    _buttons &= ~(xButton == XBUTTON1 ? MouseButtons.Extra1 : MouseButtons.Extra2);
                    Raise(MouseEventType.ExtraUp, x, y);
                    break;

                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDBLCLK:
                case WM_MBUTTONDBLCLK:
                    Raise(MouseEventType.DoubleClick, x, y);
                    break;

                case WM_MOUSEWHEEL:
                    Raise(MouseEventType.Wheel, x, y, wheel);
                    break;

                case WM_MOUSEHWHEEL:
                    Raise(MouseEventType.WheelHorizontal, x, y, wheel);
                    break;
            }
        }
        catch
        {
            // Kanca zinciri asla kırılmamalı; hatalı tek olay yutulur.
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void HandleMove(int x, int y)
    {
        if (!_dragging && (_buttons & MouseButtons.Left) != 0)
        {
            bool draggingX = Math.Abs(x - _dragStartX) > _dragThresholdX;
            bool draggingY = Math.Abs(y - _dragStartY) > _dragThresholdY;

            if (draggingX || draggingY)
            {
                _dragging = true;
                Raise(MouseEventType.DragStart, x, y);
                return;
            }
        }

        Raise(MouseEventType.Move, x, y);
    }

    /// <summary>Basma olayını yayar; çift tık aralığındaysa ayrıca çift tık üretir.</summary>
    private void RaiseClick(MouseEventType type, int x, int y)
    {
        long now = Environment.TickCount64;
        bool isDouble = now - _lastClickTicks <= GetDoubleClickTime();
        _lastClickTicks = now;

        Raise(type, x, y);

        if (isDouble)
            Raise(MouseEventType.DoubleClick, x, y);
    }

    private void Raise(MouseEventType type, int x, int y, int wheel = 0)
        => MouseActivity?.Invoke(new MouseGesture(type, x, y, _buttons, wheel));

    // ─── Klavye ───────────────────────────────────────────────────────────────

    private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || KeyActivity == null)
            return CallNextHookEx(_keyboardHook, code, wParam, lParam);

        try
        {
            int message = (int)wParam;
            bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = message is WM_KEYUP or WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = KeyInterop.KeyFromVirtualKey((int)data.VkCode);
                KeyActivity?.Invoke(new KeyGesture(key, ReadModifiers(), isDown));
            }
        }
        catch
        {
            // Kanca zinciri asla kırılmamalı.
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static System.Windows.Input.ModifierKeys ReadModifiers()
    {
        var modifiers = System.Windows.Input.ModifierKeys.None;

        if (IsDown(VK_CONTROL)) modifiers |= System.Windows.Input.ModifierKeys.Control;
        if (IsDown(VK_SHIFT)) modifiers |= System.Windows.Input.ModifierKeys.Shift;
        if (IsDown(VK_MENU)) modifiers |= System.Windows.Input.ModifierKeys.Alt;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) modifiers |= System.Windows.Input.ModifierKeys.Windows;

        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        MouseActivity = null;
        KeyActivity = null;
    }
}
