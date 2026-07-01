using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenForge.Settings;
using SfModifierKeys = ScreenForge.Settings.ModifierKeys;

namespace ScreenForge.Hotkeys;

/// <summary>
/// Global (sistem geneli) kısayolları yönetir. Görünmez bir HwndSource penceresi
/// üzerinden WM_HOTKEY mesajlarını dinler.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint VK_SNAPSHOT = 0x2C;
    private const uint VK_FN = 0xFF;
    private const uint SC_FN = 0x63;
    private const uint SC_PRINTSCREEN = 0x37;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // fsModifiers bayrakları
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly List<(HotkeyConfig config, Action callback, string displayName)> _hookHotkeys = new();
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _keyboardHook;
    private long _lastHookTriggerTick;
    private int _nextId = 1;

    /// <summary>Kayıt başarısız olan kısayollar (kullanıcı uyarısı için).</summary>
    public List<string> FailedRegistrations { get; } = new();

    public HotkeyService()
    {
        // Mesaj-yalnız (message-only) görünmez pencere.
        var parameters = new HwndSourceParameters("ScreenForgeHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _handle = _source.Handle;
    }

    /// <summary>
    /// Bir kısayolu kaydeder. Çakışma/hata olursa false döner ve FailedRegistrations'a ekler.
    /// </summary>
    public bool Register(HotkeyConfig config, Action callback, string displayName)
    {
        if (!config.IsValid) return false;

        if (ShouldUseKeyboardHook(config.Key))
        {
            if (!EnsureKeyboardHook())
            {
                FailedRegistrations.Add($"{displayName} ({config})");
                return false;
            }

            _hookHotkeys.Add((config, callback, displayName));
            return true;
        }

        uint mods = MOD_NOREPEAT;
        if (config.Modifiers.HasFlag(SfModifierKeys.Alt)) mods |= MOD_ALT;
        if (config.Modifiers.HasFlag(SfModifierKeys.Control)) mods |= MOD_CONTROL;
        if (config.Modifiers.HasFlag(SfModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (config.Modifiers.HasFlag(SfModifierKeys.Windows)) mods |= MOD_WIN;

        uint vk = KeyToVirtualKey(config.Key);
        if (vk == 0) return false;

        int id = _nextId++;
        if (RegisterHotKey(_handle, id, mods, vk))
        {
            _callbacks[id] = callback;
            return true;
        }

        FailedRegistrations.Add($"{displayName} ({config})");
        return false;
    }

    /// <summary>Tüm kayıtları kaldırır (yeniden bağlama öncesi).</summary>
    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys)
            UnregisterHotKey(_handle, id);
        _callbacks.Clear();
        _hookHotkeys.Clear();
        RemoveKeyboardHook();
        FailedRegistrations.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var cb))
            {
                handled = true;
                // UI thread'de güvenli çalıştır.
                Application.Current?.Dispatcher.BeginInvoke(cb);
            }
        }
        return IntPtr.Zero;
    }

    private static uint KeyToVirtualKey(string keyName)
    {
        if (TryParseVirtualKey(keyName, out uint customVk))
            return customVk;

        // WPF Key adı → sanal tuş kodu.
        if (Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) return (uint)vk;
        }
        return 0;
    }

    private bool EnsureKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return true;

        _keyboardProc = KeyboardHookProc;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(mod.ModuleName), 0);
        if (_keyboardHook != IntPtr.Zero)
            return true;

        _keyboardProc = null;
        return false;
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _keyboardProc = null;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int message = wParam.ToInt32();
        if (nCode >= 0 && IsKeyboardMessage(message) && _hookHotkeys.Count > 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (IsHookHotkey(data.vkCode, data.scanCode, out var keyName))
            {
                var mods = ReadCurrentModifiers();
                foreach (var item in _hookHotkeys)
                {
                    if (!IsSameHookKey(item.config.Key, keyName) || item.config.Modifiers != mods)
                        continue;

                    long now = Environment.TickCount64;
                    if (now - _lastHookTriggerTick < 500)
                        break;

                    _lastHookTriggerTick = now;
                    Application.Current?.Dispatcher.BeginInvoke(item.callback);
                    break;
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static SfModifierKeys ReadCurrentModifiers()
    {
        var mods = SfModifierKeys.None;
        if (IsDown(0xA2) || IsDown(0xA3)) mods |= SfModifierKeys.Control;
        if (IsDown(0xA0) || IsDown(0xA1)) mods |= SfModifierKeys.Shift;
        if (IsDown(0xA4) || IsDown(0xA5)) mods |= SfModifierKeys.Alt;
        if (IsDown(0x5B) || IsDown(0x5C)) mods |= SfModifierKeys.Windows;
        return mods;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;

    private static bool IsHookHotkey(uint vk, uint scanCode, out string keyName)
    {
        if (vk == VK_FN || (vk == 0 && scanCode == SC_FN))
        {
            keyName = "Fn";
            return true;
        }

        if (vk == VK_SNAPSHOT || scanCode == SC_PRINTSCREEN)
        {
            keyName = "Snapshot";
            return true;
        }

        keyName = "";
        return false;
    }

    private static bool IsFnKeyName(string keyName) => string.Equals(keyName, "Fn", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeyboardMessage(int message) =>
        message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP;

    private static bool IsSnapshotKeyName(string keyName) =>
        string.Equals(keyName, "Snapshot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(keyName, "PrintScreen", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(keyName, "PrtSc", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUseKeyboardHook(string keyName) => IsFnKeyName(keyName) || IsSnapshotKeyName(keyName);

    private static bool IsSameHookKey(string configuredKey, string actualKey)
    {
        if (IsFnKeyName(configuredKey) && IsFnKeyName(actualKey))
            return true;

        return IsSnapshotKeyName(configuredKey) && IsSnapshotKeyName(actualKey);
    }

    private static bool TryParseVirtualKey(string keyName, out uint vk)
    {
        vk = 0;
        if (!keyName.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
            return false;

        return uint.TryParse(keyName[3..], System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out vk);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
