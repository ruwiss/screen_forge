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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // fsModifiers bayrakları
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private readonly Dictionary<int, Action> _callbacks = new();
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
        // WPF Key adı → sanal tuş kodu.
        if (Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) return (uint)vk;
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
