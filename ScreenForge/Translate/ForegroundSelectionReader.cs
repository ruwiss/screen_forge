using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenForge.Translate;

/// <summary>
/// Kısayol basıldığında öndeki uygulamadaki seçili metni alır.
/// Kendi TextBox'ımızdaysak seçimi doğrudan okur; değilse Ctrl+C gönderir.
/// </summary>
internal static class ForegroundSelectionReader
{
    internal const int MaxChars = 8000;

    private const uint KeyeventfKeyUp = 0x0002;
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12;
    private const byte VkLWin = 0x5B;
    private const byte VkRWin = 0x5C;
    private const byte VkC = 0x43;

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    public static async Task<string?> TryCaptureAsync(CancellationToken ct = default)
    {
        try
        {
            if (IsOwnForeground() && Keyboard.FocusedElement is TextBox { SelectedText.Length: > 0 } box)
                return Normalize(box.SelectedText);

            await WaitForModifiersAsync(ct).ConfigureAwait(true);
            return await TryCopySelectionAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    internal static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length == 0)
            return null;

        return text.Length <= MaxChars ? text : text[..MaxChars];
    }

    private static async Task WaitForModifiersAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 400)
        {
            ct.ThrowIfCancellationRequested();
            if (!AnyModifierDown())
                return;
            await Task.Delay(20, ct).ConfigureAwait(true);
        }

        ReleaseExtraModifiers();
        await Task.Delay(30, ct).ConfigureAwait(true);
    }

    private static async Task<string?> TryCopySelectionAsync(CancellationToken ct)
    {
        uint before;
        try
        {
            before = GetClipboardSequenceNumber();
        }
        catch
        {
            return null;
        }

        SendCopyChord();

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 280)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (GetClipboardSequenceNumber() != before)
                    break;
            }
            catch
            {
                break;
            }

            await Task.Delay(20, ct).ConfigureAwait(true);
        }

        try
        {
            if (GetClipboardSequenceNumber() == before)
                return null;
        }
        catch
        {
            return null;
        }

        try
        {
            if (!Clipboard.ContainsText())
                return null;
            return Normalize(Clipboard.GetText());
        }
        catch
        {
            return null;
        }
    }

    private static bool IsOwnForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == GetCurrentProcessId();
        }
        catch
        {
            return false;
        }
    }

    private static bool AnyModifierDown()
        => IsDown(VkShift) || IsDown(VkControl) || IsDown(VkMenu) || IsDown(VkLWin) || IsDown(VkRWin);

    private static bool IsDown(int vk)
    {
        try
        {
            return (GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseExtraModifiers()
    {
        KeyUp(VkShift);
        KeyUp(VkMenu);
        KeyUp(VkLWin);
        KeyUp(VkRWin);
    }

    private static void SendCopyChord()
    {
        ReleaseExtraModifiers();
        KeyDown(VkControl);
        KeyDown(VkC);
        KeyUp(VkC);
        KeyUp(VkControl);
    }

    private static void KeyDown(byte vk) => SendKey(vk, 0);

    private static void KeyUp(byte vk) => SendKey(vk, KeyeventfKeyUp);

    private static void SendKey(byte vk, uint flags)
    {
        try
        {
            keybd_event(vk, 0, flags, UIntPtr.Zero);
        }
        catch
        {
            // Tuş gönderilemezse seçim boş kalır.
        }
    }
}
