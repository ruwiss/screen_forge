using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenForge.Settings;
using SfModifierKeys = ScreenForge.Settings.ModifierKeys;

namespace ScreenForge.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onHotkeysChanged;
    private bool _loading;

    // ── Hotkey recording state ──
    private HwndSource? _hwndSource;
    private TextBlock? _recLabel;
    private Border? _recBorder;
    private HotkeyConfig? _recConfig;
    private string _recSavedKey = "";
    private SfModifierKeys _recSavedMods;

    private static readonly SolidColorBrush _hkBg = new(Color.FromRgb(0x27, 0x2D, 0x3B));
    private static readonly SolidColorBrush _hkBorder = new(Color.FromRgb(0x3A, 0x42, 0x54));
    private static readonly SolidColorBrush _hkActiveBg = new(Color.FromRgb(0x2A, 0x20, 0x10));
    private static readonly SolidColorBrush _hkActiveBorder = new(Color.FromRgb(0xEA, 0x6F, 0x12));
    private static readonly SolidColorBrush _hkDimFg = new(Color.FromRgb(0x9A, 0xA4, 0xB8));

    public SettingsWindow(AppSettings settings, Action onHotkeysChanged)
    {
        InitializeComponent();
        _settings = settings;
        _onHotkeysChanged = onHotkeysChanged;
        LoadValues();
        WireEvents();
        BuildHotkeyPanel();
        BtnClose.Click += (_, _) => Close();
        Loaded += (_, _) => FixTabHeight();
        SourceInitialized += (_, e) =>
        {
            DarkTitleBar.Apply(this);
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);
        };
        Closed += (_, _) =>
        {
            _hwndSource?.RemoveHook(WndProc);
            RemoveKeyboardHook();
        };
    }

    // ═══════════════════════════════════════════
    //  Win32 message hook — captures ALL keys
    // ═══════════════════════════════════════════

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WH_KEYBOARD_LL = 13;
    private const uint VK_SNAPSHOT = 0x2C;
    private const uint VK_FN = 0xFF;
    private const uint SC_FN = 0x63;
    private const uint SC_PRINTSCREEN = 0x37;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_recConfig == null) return IntPtr.Zero;
        if (msg is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return IntPtr.Zero;

        int vk = wParam.ToInt32();

        if (IsModifierOnly(vk))
            return IntPtr.Zero;

        handled = true;

        // ESC → cancel
        if (vk == 0x1B)
        {
            Dispatcher.InvokeAsync(() => FinishRecording(true));
            return IntPtr.Zero;
        }

        _recConfig.Modifiers = ReadCurrentModifiers();
        _recConfig.Key = GetKeyName((uint)vk, 0);
        Dispatcher.InvokeAsync(() => FinishRecording(false));
        return IntPtr.Zero;
    }

    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _keyboardHookProc;

    private void EnsureKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        _keyboardHookProc = KeyboardHookProc;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(mod.ModuleName), 0);
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _keyboardHookProc = null;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int message = wParam.ToInt32();
        if (_recConfig == null || nCode < 0 || !IsKeyboardMessage(message))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (IsModifierOnly((int)data.vkCode))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        if (data.vkCode == 0x1B)
        {
            Dispatcher.Invoke(() => FinishRecording(true));
            return new IntPtr(1);
        }

        var keyName = GetKeyName(data.vkCode, data.scanCode);
        Dispatcher.Invoke(() =>
        {
            if (_recConfig == null)
                return;

            _recConfig.Modifiers = ReadCurrentModifiers();
            _recConfig.Key = keyName;
            FinishRecording(false);
        });
        return new IntPtr(1);
    }

    private static bool IsModifierOnly(int vk) =>
        vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C
            or 0x10 or 0x11 or 0x12;

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

    private static string GetKeyName(uint vk, uint scanCode)
    {
        if (vk == VK_FN || (vk == 0 && scanCode == SC_FN))
            return "Fn";
        if (vk == VK_SNAPSHOT || scanCode == SC_PRINTSCREEN)
            return "Snapshot";

        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        return key != Key.None ? key.ToString() : $"VK_{vk:X2}";
    }

    private static bool IsKeyboardMessage(int message) =>
        message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP;

    // ═══════════════════════════════════════════
    //  Hotkey panel builder (code-gen, no XAML names needed)
    // ═══════════════════════════════════════════

    private void BuildHotkeyPanel()
    {
        var items = new (string title, string desc, HotkeyConfig config)[]
        {
            ("Bölge yakalama", "Seçili alanın ekran görüntüsünü al", _settings.RegionHotkey),
            ("Tam ekran yakalama", "Tüm ekranın görüntüsünü al", _settings.FullScreenHotkey),
            ("Anında yükleme", "Ekran görüntüsünü al ve yükle", _settings.FullScreenUploadHotkey),
            ("Serbest / yerleştirme", "Kolaj ve serbest düzenleme modu", _settings.CollageHotkey),
        };

        var cardStyle = (Style)FindResource("Card");
        var labelStyle = (Style)FindResource("Label");
        var mutedStyle = (Style)FindResource("Muted");
        var btnStyle = (Style)FindResource("SecondaryButton");
        var textBrush = (Brush)FindResource("TextBrush");

        foreach (var (title, desc, config) in items)
        {
            var hkLabel = new TextBlock
            {
                Text = config.ToString(),
                Foreground = textBrush,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var hkBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Background = _hkBg,
                BorderBrush = _hkBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 130,
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = hkLabel,
            };

            var resetBtn = new Button
            {
                Content = "Sıfırla",
                Style = btnStyle,
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            // Click → start recording
            hkBorder.MouseLeftButtonDown += (_, e) =>
            {
                BeginRecording(hkLabel, hkBorder, config);
                e.Handled = true;
            };

            // Reset
            var cfg = config; // capture
            var lbl = hkLabel;
            var brd = hkBorder;
            resetBtn.Click += (_, _) =>
            {
                if (_recConfig == cfg) FinishRecording(true);
                cfg.Key = "";
                cfg.Modifiers = SfModifierKeys.None;
                lbl.Text = cfg.ToString();
                lbl.Foreground = Brushes.White;
                brd.BorderBrush = _hkBorder;
                brd.Background = _hkBg;
                Apply(() => { });
            };

            // Layout
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            infoStack.Children.Add(new TextBlock { Text = title, Style = labelStyle });
            infoStack.Children.Add(new TextBlock { Text = desc, Style = mutedStyle, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) });

            var dock = new DockPanel();
            DockPanel.SetDock(resetBtn, Dock.Right);
            DockPanel.SetDock(hkBorder, Dock.Right);
            dock.Children.Add(resetBtn);
            dock.Children.Add(hkBorder);
            dock.Children.Add(infoStack);

            var card = new Border { Style = cardStyle, Padding = new Thickness(12, 10, 12, 10) };
            card.Child = dock;
            HotkeyPanel.Children.Add(card);
        }

        var hint = new TextBlock
        {
            Style = mutedStyle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2, 0, 0),
            FontSize = 11,
            Text = "Tıklayıp tuş kombinasyonuna basın · ESC iptal",
        };
        HotkeyPanel.Children.Add(hint);

        // Click anywhere else → cancel recording
        MouseDown += (_, _) =>
        {
            if (_recConfig != null) FinishRecording(true);
        };
    }

    private void BeginRecording(TextBlock label, Border border, HotkeyConfig config)
    {
        if (_recConfig != null) FinishRecording(true);
        _recLabel = label;
        _recBorder = border;
        _recConfig = config;
        _recSavedKey = config.Key;
        _recSavedMods = config.Modifiers;
        EnsureKeyboardHook();
        label.Text = "Tuş basın...";
        label.Foreground = _hkDimFg;
        border.BorderBrush = _hkActiveBorder;
        border.Background = _hkActiveBg;
    }

    private void FinishRecording(bool cancel)
    {
        if (_recLabel == null || _recBorder == null || _recConfig == null) return;
        if (cancel)
        {
            _recConfig.Key = _recSavedKey;
            _recConfig.Modifiers = _recSavedMods;
        }
        _recLabel.Text = _recConfig.ToString();
        _recLabel.Foreground = Brushes.White;
        _recBorder.BorderBrush = _hkBorder;
        _recBorder.Background = _hkBg;
        var didChange = !cancel;
        _recLabel = null;
        _recBorder = null;
        _recConfig = null;
        RemoveKeyboardHook();
        if (didChange) Apply(() => { });
    }

    // ═══════════════════════════════════════════
    //  Rest of settings (unchanged logic)
    // ═══════════════════════════════════════════

    private void FixTabHeight()
    {
        double maxH = 0;
        int saved = TabCtrl.SelectedIndex;
        for (int i = 0; i < TabCtrl.Items.Count; i++)
        {
            TabCtrl.SelectedIndex = i;
            TabCtrl.UpdateLayout();
            if (TabCtrl.Items[i] is TabItem ti && ti.Content is FrameworkElement fe)
            {
                fe.Measure(new Size(TabCtrl.ActualWidth, double.PositiveInfinity));
                maxH = Math.Max(maxH, fe.DesiredSize.Height);
            }
        }
        TabCtrl.SelectedIndex = saved;
        foreach (TabItem ti in TabCtrl.Items)
            if (ti.Content is FrameworkElement fe)
                fe.MinHeight = maxH;
    }

    private void LoadValues()
    {
        _loading = true;
        ChkShowCursor.IsChecked = _settings.ShowCursor;
        ChkAutoCopy.IsChecked = _settings.AutoCopyLinkAfterUpload;
        ChkAutoClose.IsChecked = _settings.AutoCloseUploadWindow;
        SyncAutoCloseState();
        ChkStartup.IsChecked = _settings.LaunchAtStartup;

        CmbFormat.SelectedIndex = (int)_settings.OutputFormat;
        SldQuality.Value = _settings.Quality;
        QualityValue.Text = _settings.Quality.ToString();
        TxtSaveDir.Text = _settings.SaveDirectory;
        UpdateQualityVisibility();
        _loading = false;
    }

    private void WireEvents()
    {
        ChkShowCursor.Click += (_, _) => Apply(() => _settings.ShowCursor = ChkShowCursor.IsChecked == true);
        ChkAutoCopy.Click += (_, _) => Apply(() =>
        {
            _settings.AutoCopyLinkAfterUpload = ChkAutoCopy.IsChecked == true;
            if (!_settings.AutoCopyLinkAfterUpload) { ChkAutoClose.IsChecked = false; _settings.AutoCloseUploadWindow = false; }
            SyncAutoCloseState();
        });
        ChkAutoClose.Click += (_, _) => Apply(() => _settings.AutoCloseUploadWindow = ChkAutoClose.IsChecked == true);
        ChkStartup.Click += (_, _) => Apply(() =>
        {
            _settings.LaunchAtStartup = ChkStartup.IsChecked == true;
            StartupManager.SetEnabled(_settings.LaunchAtStartup);
        });

        CmbFormat.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.OutputFormat = (ImageFormat)CmbFormat.SelectedIndex;
            UpdateQualityVisibility();
        });
        SldQuality.ValueChanged += (_, _) => Apply(() =>
        {
            _settings.Quality = (int)SldQuality.Value;
            QualityValue.Text = _settings.Quality.ToString();
        });
        BtnBrowse.Click += (_, _) => BrowseFolder();
    }

    private void SyncAutoCloseState()
    {
        bool enabled = _settings.AutoCopyLinkAfterUpload;
        ChkAutoClose.IsEnabled = enabled;
        ChkAutoClose.Opacity = enabled ? 1.0 : 0.4;
    }

    private void UpdateQualityVisibility()
    {
        QualityPanel.Visibility = _settings.OutputFormat == ImageFormat.Png
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kayıt klasörü seçin",
            InitialDirectory = _settings.SaveDirectory,
        };
        if (dlg.ShowDialog() == true)
        {
            _settings.SaveDirectory = dlg.FolderName;
            TxtSaveDir.Text = dlg.FolderName;
            Apply(() => { });
        }
    }

    private void Apply(Action change)
    {
        if (_loading) return;
        change();
        _settings.Save();
        _onHotkeysChanged();
    }
}
