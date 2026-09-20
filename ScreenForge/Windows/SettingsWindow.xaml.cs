using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenForge.Settings;
using ScreenForge.Record;
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
    private string? _presenterSel;
    private readonly Dictionary<string, Border> _presenterTiles = new();

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
        BuildPresenterTiles();
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
            ("Hızlı çeviri", "Seçili metni çevirir; seçim yoksa yazarak çeviri açılır", _settings.QuickTranslateHotkey),
        };

        foreach (var (title, desc, config) in items)
            AddHotkeyRow(HotkeyPanel, title, desc, config);

        var hint = new TextBlock
        {
            Style = (Style)FindResource("Muted"),
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

    private void BuildPresenterTiles()
    {
        PresenterTileHost.Children.Clear();
        _presenterTiles.Clear();
        var p = _settings.Presenter;
        AddPresenterTile("pen", "Kalem", p.PenEnabled, p.PenHotkey, v => p.PenEnabled = v);
        AddPresenterTile("laser", "Lazer", p.LaserEnabled, p.LaserHotkey, v => p.LaserEnabled = v);
        AddPresenterTile("rect", "Dikdörtgen", p.RectangleEnabled, p.RectangleHotkey, v => p.RectangleEnabled = v);
        AddPresenterTile("ell", "Daire", p.EllipseEnabled, p.EllipseHotkey, v => p.EllipseEnabled = v);
        AddPresenterTile("arrow", "Ok", p.ArrowEnabled, p.ArrowHotkey, v => p.ArrowEnabled = v);
        AddPresenterTile("line", "Çizgi", p.LineEnabled, p.LineHotkey, v => p.LineEnabled = v);
        AddPresenterTile("spot", "Spotlight", p.SpotlightEnabled, p.SpotlightHotkey, v => p.SpotlightEnabled = v);
        AddPresenterTile("cancel", "İptal", true, p.CancelHotkey, _ => p.CancelEnabled = true, locked: true);
        PaintPresenterSelection();
        FillPresenterDetail();
    }

    private void AddPresenterTile(string id, string title, bool on, HotkeyConfig hk, Action<bool> setOn, bool locked = false)
    {
        if (locked) on = true;
        var card = new Border
        {
            Margin = new Thickness(3),
            Padding = new Thickness(8, 7, 8, 7),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x2D, 0x3B)),
            BorderBrush = _hkBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Opacity = on ? 1 : 0.5,
            Tag = id,
        };
        var keyTb = new TextBlock
        {
            Text = on && hk.IsValid ? hk.ToString() : "kapalı",
            Style = (Style)FindResource("Muted"),
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var head = new DockPanel();
        if (!locked)
        {
            var sw = MakeSwitch(on, v =>
            {
                setOn(v);
                card.Opacity = v ? 1 : 0.5;
                keyTb.Text = v && hk.IsValid ? hk.ToString() : "kapalı";
                _presenterSel = id;
                PaintPresenterSelection();
                Apply(() => { });
            });
            DockPanel.SetDock(sw, Dock.Right);
            head.Children.Add(sw);
        }
        head.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("Label"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(keyTb);
        card.Child = body;
        card.MouseLeftButtonDown += (_, e) =>
        {
            _presenterSel = id;
            PaintPresenterSelection();
            FillPresenterDetail();
            e.Handled = true;
        };
        _presenterTiles[id] = card;
        PresenterTileHost.Children.Add(card);
    }

    private void PaintPresenterSelection()
    {
        foreach (var (id, card) in _presenterTiles)
        {
            bool selected = _presenterSel == id;
            card.Background = new SolidColorBrush(selected ? Color.FromRgb(0x2A, 0x20, 0x10) : Color.FromRgb(0x1F, 0x24, 0x30));
            card.BorderBrush = selected ? _hkActiveBorder : _hkBorder;
        }
    }

    private Border MakeSwitch(bool on, Action<bool> set)
    {
        bool value = on;
        var thumb = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var track = new Border
        {
            Width = 30,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Child = thumb,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        void Paint()
        {
            thumb.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            track.Background = new SolidColorBrush(value
                ? Color.FromRgb(0xEA, 0x6F, 0x12)
                : Color.FromRgb(0x3A, 0x42, 0x54));
        }
        Paint();
        track.PreviewMouseLeftButtonDown += (_, e) =>
        {
            value = !value;
            Paint();
            set(value);
            e.Handled = true;
        };
        return track;
    }

    private void FillPresenterDetail()
    {
        PresenterDetailPanel.Children.Clear();
        if (_presenterSel == null)
        {
            PresenterDetailCard.Visibility = Visibility.Collapsed;
            return;
        }
        PresenterDetailCard.Visibility = Visibility.Visible;
        var p = _settings.Presenter;
        switch (_presenterSel)
        {
            case "pen":
                PresenterDetailPanel.Children.Add(DetailTitle("Kalem"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.PenHotkey));
                PresenterDetailPanel.Children.Add(InkPalette());
                PresenterDetailPanel.Children.Add(LabeledSlider("Kalınlık", p.PenWidth, 1, 16, v =>
                {
                    p.PenWidth = v;
                    return ((int)v).ToString();
                }));
                break;
            case "laser":
                PresenterDetailPanel.Children.Add(DetailTitle("Lazer"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.LaserHotkey));
                PresenterDetailPanel.Children.Add(ColorAndWidth("Kalınlık", p.LaserColor, p.LaserWidth, 1, 16,
                    c => p.LaserColor = c, v => p.LaserWidth = v));
                PresenterDetailPanel.Children.Add(LabeledSlider("Sönme", p.LaserFadeMs, 150, 2000, v =>
                {
                    p.LaserFadeMs = (int)v;
                    return FadeLabel(p.LaserFadeMs);
                }));
                break;
            case "rect":
                PresenterDetailPanel.Children.Add(DetailTitle("Dikdörtgen"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.RectangleHotkey));
                PresenterDetailPanel.Children.Add(InkPalette());
                break;
            case "ell":
                PresenterDetailPanel.Children.Add(DetailTitle("Daire"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.EllipseHotkey));
                PresenterDetailPanel.Children.Add(InkPalette());
                break;
            case "arrow":
                PresenterDetailPanel.Children.Add(DetailTitle("Ok"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.ArrowHotkey));
                PresenterDetailPanel.Children.Add(InkPalette());
                PresenterDetailPanel.Children.Add(new TextBlock
                {
                    Text = "Eğim — yalnızca ok çizerken",
                    Style = (Style)FindResource("Muted"),
                    FontSize = 11,
                    Margin = new Thickness(0, 10, 0, 4),
                });
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.ArrowBendHotkey));
                break;
            case "line":
                PresenterDetailPanel.Children.Add(DetailTitle("Çizgi"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.LineHotkey));
                PresenterDetailPanel.Children.Add(InkPalette());
                break;
            case "spot":
                PresenterDetailPanel.Children.Add(DetailTitle("Spotlight"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.SpotlightHotkey));
                PresenterDetailPanel.Children.Add(LabeledSlider("Boyut", p.SpotlightRadius, 60, 400, v =>
                {
                    p.SpotlightRadius = v;
                    return ((int)v).ToString();
                }));
                PresenterDetailPanel.Children.Add(LabeledSlider("Kenar", p.SpotlightSoftness, 8, 70, v =>
                {
                    p.SpotlightSoftness = v;
                    return ((int)v).ToString();
                }));
                PresenterDetailPanel.Children.Add(LabeledSlider("Karartma", p.SpotlightDim, 0.2, 0.8, v =>
                {
                    p.SpotlightDim = v;
                    return $"{(int)(v * 100)}%";
                }));
                break;
            case "cancel":
                PresenterDetailPanel.Children.Add(DetailTitle("İptal"));
                PresenterDetailPanel.Children.Add(MakeHotkeyChip(p.CancelHotkey));
                break;
        }
    }

    private TextBlock DetailTitle(string t) => new()
    {
        Text = t,
        Style = (Style)FindResource("Label"),
        Margin = new Thickness(0, 0, 0, 8),
    };

    private UIElement InkPalette()
    {
        var p = _settings.Presenter;
        p.EnsureInkColors();
        var col = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
        var grid = new UniformGrid { Columns = 5, Rows = 1 };
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var fill = ParseHex(p.InkColors[idx], Color.FromRgb(0xEA, 0x6F, 0x12));
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(fill),
                BorderBrush = SwatchEdge(fill),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            swatch.MouseLeftButtonUp += (_, e) =>
            {
                var current = (swatch.Background as SolidColorBrush)?.Color ?? fill;
                PickColor(swatch, current, c =>
                {
                    p.InkColors[idx] = ToHex(c);
                    if (idx == 0) p.PenColor = p.InkColors[idx];
                    swatch.Background = new SolidColorBrush(c);
                    swatch.BorderBrush = SwatchEdge(c);
                    Apply(() => { });
                });
                e.Handled = true;
            };
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            cell.Children.Add(swatch);
            cell.Children.Add(new TextBlock
            {
                Text = $"{idx + 1}",
                Style = (Style)FindResource("Muted"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
            });
            grid.Children.Add(cell);
        }
        col.Children.Add(grid);
        col.Children.Add(new TextBlock
        {
            Text = "Çizerken 1–5 tuşuyla renk değiştir.",
            Style = (Style)FindResource("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        return col;
    }

    private static SolidColorBrush SwatchEdge(Color c)
    {
        int lum = (c.R * 3 + c.G * 6 + c.B) / 10;
        return lum > 200
            ? new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x78))
            : new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54));
    }

    private UIElement ColorAndWidth(string widthLabel, string hex, double width, double min, double max,
        Action<string> setColor, Action<double> setWidth)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var swatch = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
            BorderBrush = _hkBorder, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(ParseHex(hex, Color.FromRgb(0xEA, 0x6F, 0x12))),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        swatch.MouseLeftButtonUp += (_, e) =>
        {
            var current = (swatch.Background as SolidColorBrush)?.Color
                ?? ParseHex(hex, Color.FromRgb(0xEA, 0x6F, 0x12));
            PickColor(swatch, current, c =>
            {
                setColor(ToHex(c));
                swatch.Background = new SolidColorBrush(c);
                Apply(() => { });
            });
            e.Handled = true;
        };
        DockPanel.SetDock(swatch, Dock.Right);
        row.Children.Add(swatch);
        row.Children.Add(LabeledSlider(widthLabel, width, min, max, v =>
        {
            setWidth(v);
            return ((int)v).ToString();
        }));
        return row;
    }

    private DockPanel LabeledSlider(string label, double value, double min, double max, Func<double, string> apply)
    {
        var caption = new TextBlock
        {
            Style = (Style)FindResource("Label"),
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center,
            Text = $"{label} {apply(value)}",
        };
        var sld = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center };
        var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(caption, Dock.Left);
        dock.Children.Add(caption);
        dock.Children.Add(sld);
        sld.ValueChanged += (_, _) =>
        {
            caption.Text = $"{label} {apply(sld.Value)}";
            Apply(() => { });
        };
        return dock;
    }

    private void AddHotkeyRow(StackPanel panel, string title, string desc, HotkeyConfig config)
    {
        var cardStyle = (Style)FindResource("Card");
        var labelStyle = (Style)FindResource("Label");
        var mutedStyle = (Style)FindResource("Muted");
        var btnStyle = (Style)FindResource("SecondaryButton");
        var textBrush = (Brush)FindResource("TextBrush");

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

        hkBorder.MouseLeftButtonDown += (_, e) =>
        {
            BeginRecording(hkLabel, hkBorder, config);
            e.Handled = true;
        };

        var cfg = config;
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

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        infoStack.Children.Add(new TextBlock { Text = title, Style = labelStyle });
        if (!string.IsNullOrEmpty(desc))
            infoStack.Children.Add(new TextBlock { Text = desc, Style = mutedStyle, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) });

        var dock = new DockPanel();
        DockPanel.SetDock(resetBtn, Dock.Right);
        DockPanel.SetDock(hkBorder, Dock.Right);
        dock.Children.Add(resetBtn);
        dock.Children.Add(hkBorder);
        dock.Children.Add(infoStack);

        var card = new Border { Style = cardStyle, Padding = new Thickness(12, 10, 12, 10) };
        card.Child = dock;
        panel.Children.Add(card);
    }

    private Border MakeHotkeyChip(HotkeyConfig config)
    {
        var textBrush = (Brush)FindResource("TextBrush");
        var hkLabel = new TextBlock
        {
            Text = config.ToString(),
            Foreground = textBrush,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hkBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Background = _hkBg,
            BorderBrush = _hkBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            MinWidth = 108,
            Child = hkLabel,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Tıkla: ata · sağ tık: sil",
        };
        hkBorder.MouseLeftButtonDown += (_, e) =>
        {
            BeginRecording(hkLabel, hkBorder, config);
            e.Handled = true;
        };
        hkBorder.MouseRightButtonDown += (_, e) =>
        {
            if (_recConfig == config) FinishRecording(true);
            config.Key = "";
            config.Modifiers = SfModifierKeys.None;
            hkLabel.Text = config.ToString();
            Apply(() => { });
            e.Handled = true;
        };
        return hkBorder;
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
        var recCfg = _recConfig;
        _recLabel = null;
        _recBorder = null;
        _recConfig = null;
        RemoveKeyboardHook();
        if (didChange)
        {
            Apply(() => { });
            if (_presenterSel != null)
            {
                FillPresenterDetail();
                if (_presenterTiles.TryGetValue(_presenterSel, out var card)
                    && card.Child is StackPanel sp && sp.Children.Count > 1
                    && sp.Children[1] is TextBlock keyTb)
                    keyTb.Text = recCfg.IsValid ? recCfg.ToString() : "kapalı";
            }
        }
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
        maxH = Math.Min(maxH, 460);
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
        LoadRecordValues();
        FillTranslateLanguageCombos();
        _loading = false;
    }

    private void LoadRecordValues()
    {
        SelectIntTag(CmbGifFps, _settings.Gif.Fps, 20);
        ChkGifCursor.IsChecked = _settings.Gif.CaptureCursor;
        SldGifMemory.Value = Math.Clamp(_settings.Gif.MaxFrameMemoryMb, 64, 2048);
        GifMemoryValue.Text = $"{(int)SldGifMemory.Value} MB";
        SelectIntTag(CmbVideoFps, _settings.Video.Fps, 30);
        CmbVideoQuality.SelectedIndex = _settings.Video.Quality switch
        {
            VideoQuality.Low => 0,
            VideoQuality.High => 2,
            _ => 1,
        };
        ChkVideoCursor.IsChecked = _settings.Video.CaptureCursor;
        ChkVideoClicks.IsChecked = _settings.Video.HighlightClicks;
        ChkSystemAudio.IsChecked = _settings.Video.RecordSystemAudio;
        ChkMicrophone.IsChecked = _settings.Video.RecordMicrophone;
        ChkCountdown.IsChecked = _settings.Video.ShowCountdown;
        FillMicrophones();
        SyncMicRow();
    }

    private void FillMicrophones()
    {
        CmbMicrophone.Items.Clear();
        var devices = AudioDevices.ListMicrophones();
        if (devices.Count == 0)
        {
            CmbMicrophone.Items.Add(new ComboBoxItem { Content = "Mikrofon yok", Tag = "" });
            CmbMicrophone.SelectedIndex = 0;
            return;
        }

        int pick = 0;
        string want = _settings.Video.MicDeviceId;
        if (string.IsNullOrWhiteSpace(want))
            want = AudioDevices.DefaultMicrophoneId() ?? "";
        for (int i = 0; i < devices.Count; i++)
        {
            CmbMicrophone.Items.Add(new ComboBoxItem { Content = devices[i].Name, Tag = devices[i].Id });
            if (devices[i].Id == want) pick = i;
        }
        CmbMicrophone.SelectedIndex = pick;
        if (string.IsNullOrWhiteSpace(_settings.Video.MicDeviceId) && devices.Count > 0)
            _settings.Video.MicDeviceId = devices[pick].Id;
    }

    private void SyncMicRow()
    {
        bool on = _settings.Video.RecordMicrophone;
        MicRow.IsEnabled = on;
        MicRow.Opacity = on ? 1 : 0.45;
    }

    private static void SelectIntTag(ComboBox cmb, int value, int fallback)
    {
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            if (cmb.Items[i] is ComboBoxItem item && int.TryParse(Convert.ToString(item.Tag), out int t) && t == value)
            {
                cmb.SelectedIndex = i;
                return;
            }
        }
        if (value != fallback)
        {
            SelectIntTag(cmb, fallback, fallback);
            return;
        }
        if (cmb.Items.Count > 0)
            cmb.SelectedIndex = 0;
    }

    private static int GetIntTag(ComboBox cmb, int fallback)
    {
        if (cmb.SelectedItem is ComboBoxItem item && int.TryParse(Convert.ToString(item.Tag), out int t))
            return t;
        return fallback;
    }

    private void FillTranslateLanguageCombos()
    {
        CmbTranslateNative.Items.Clear();
        CmbTranslatePair.Items.Clear();
        foreach (var (code, label) in TranslateLanguageDefaults.Languages)
        {
            CmbTranslateNative.Items.Add(new ComboBoxItem { Content = label, Tag = code });
            CmbTranslatePair.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        }

        SelectLangCombo(CmbTranslateNative, _settings.TranslateNativeLanguage, "en");
        SelectLangCombo(CmbTranslatePair, _settings.TranslatePairLanguage,
            TranslateLanguageDefaults.DefaultPair(_settings.TranslateNativeLanguage));
    }

    private static void SelectLangCombo(ComboBox cmb, string code, string fallback)
    {
        string want = string.IsNullOrWhiteSpace(code) ? fallback : code.Trim().ToLowerInvariant();
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            if (cmb.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, want, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedIndex = i;
                return;
            }
        }
        // Bilinmeyen kod: listeye ekle
        cmb.Items.Add(new ComboBoxItem { Content = want, Tag = want });
        cmb.SelectedIndex = cmb.Items.Count - 1;
    }

    private static string? GetLangComboCode(ComboBox cmb)
        => (cmb.SelectedItem as ComboBoxItem)?.Tag as string;

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

        CmbGifFps.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.Gif.Fps = GetIntTag(CmbGifFps, 20);
        });
        ChkGifCursor.Click += (_, _) => Apply(() => _settings.Gif.CaptureCursor = ChkGifCursor.IsChecked == true);
        SldGifMemory.ValueChanged += (_, _) => Apply(() =>
        {
            _settings.Gif.MaxFrameMemoryMb = (int)SldGifMemory.Value;
            GifMemoryValue.Text = $"{_settings.Gif.MaxFrameMemoryMb} MB";
        });
        CmbVideoFps.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.Video.Fps = GetIntTag(CmbVideoFps, 30);
        });
        CmbVideoQuality.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.Video.Quality = CmbVideoQuality.SelectedIndex switch
            {
                0 => VideoQuality.Low,
                2 => VideoQuality.High,
                _ => VideoQuality.Medium,
            };
        });
        ChkVideoCursor.Click += (_, _) => Apply(() => _settings.Video.CaptureCursor = ChkVideoCursor.IsChecked == true);
        ChkVideoClicks.Click += (_, _) => Apply(() => _settings.Video.HighlightClicks = ChkVideoClicks.IsChecked == true);
        ChkCountdown.Click += (_, _) => Apply(() => _settings.Video.ShowCountdown = ChkCountdown.IsChecked == true);
        ChkSystemAudio.Click += (_, _) => Apply(() => _settings.Video.RecordSystemAudio = ChkSystemAudio.IsChecked == true);
        ChkMicrophone.Click += (_, _) => Apply(() =>
        {
            _settings.Video.RecordMicrophone = ChkMicrophone.IsChecked == true;
            SyncMicRow();
        });
        CmbMicrophone.SelectionChanged += (_, _) => Apply(() =>
        {
            if (CmbMicrophone.SelectedItem is ComboBoxItem item && item.Tag is string id)
                _settings.Video.MicDeviceId = id;
        });

        CmbTranslateNative.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.TranslateNativeLanguage = GetLangComboCode(CmbTranslateNative) ?? "en";
        });
        CmbTranslatePair.SelectionChanged += (_, _) => Apply(() =>
        {
            _settings.TranslatePairLanguage = GetLangComboCode(CmbTranslatePair) ?? "en";
        });
    }

    private void PickColor(UIElement target, Color initial, Action<Color> onPicked)
    {
        var popup = new ColorPickerPopup(target, initial, onPicked);
        popup.Open();
    }

    private static Color ParseHex(string hex, Color fallback)
    {
        var sk = Editor.InteractiveCanvas.ColorFromHex(hex);
        return sk.Alpha == 0 ? fallback : Color.FromArgb(sk.Alpha, sk.Red, sk.Green, sk.Blue);
    }

    private static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string FadeLabel(int ms) => $"{ms / 1000.0:0.#} sn";

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
