using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace ScreenForge.Tray;

/// <summary>
/// Sistem tepsisi ikonu ve sağ-tık menüsü. Uygulamanın görünen tek kalıcı yüzeyi.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;

    public event Action? CaptureRegionRequested;
    public event Action? CaptureFullScreenRequested;
    public event Action? CollageRequested;
    public event Action? ColorPickerRequested;
    public event Action? TrayUploadRequested;
    public event Action? QuickTranslateRequested;
    public event Action? SettingsRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;
    public event Action<bool>? PresenterEnabledChanged;
    public Func<bool>? GetPresenterEnabled { get; set; }
    public event Action<bool>? SubtitleEnabledChanged;
    public Func<bool>? GetSubtitleEnabled { get; set; }

    public TrayIconService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "ScreenForge",
            Icon = LoadIconNative(),
        };

        _icon.ContextMenu = BuildMenu();
        // Sol tık: bölge yakalama
        _icon.LeftClickCommand = new RelayCommandLite(() => CaptureRegionRequested?.Invoke());
        _icon.ForceCreate();
    }

    private static System.Drawing.Icon LoadIconNative()
    {
        var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        var sri = Application.GetResourceStream(uri);
        return new System.Drawing.Icon(sri!.Stream, new System.Drawing.Size(256, 256));
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem("Bölge Yakala", () => CaptureRegionRequested?.Invoke(), 0xE7A8, isDefault: true));
        menu.Items.Add(MenuItem("Tam Ekran Yakala", () => CaptureFullScreenRequested?.Invoke(), 0xE740));
        menu.Items.Add(MenuItem("Serbest / Yerleştirme", () => CollageRequested?.Invoke(), 0xE8B9));
        menu.Items.Add(MenuItem("Renk Seçici", () => ColorPickerRequested?.Invoke(), 0xE790));
        menu.Items.Add(MenuItem("Yükleme Yap", () => TrayUploadRequested?.Invoke(), 0xE898));
        menu.Items.Add(MenuItem("Hızlı Çeviri", () => QuickTranslateRequested?.Invoke(), 0xE8C1));
        menu.Items.Add(new Separator());
        menu.Items.Add(PresenterToggle());
        menu.Items.Add(SubtitleToggle());
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Ayarlar", () => SettingsRequested?.Invoke(), 0xE713));
        menu.Items.Add(MenuItem("Hakkında", () => AboutRequested?.Invoke(), 0xE946));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Çıkış", () => ExitRequested?.Invoke(), 0xE711));

        menu.Opened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag as string == "presenter")
                    PaintToggle(item, "Sunum araçları", GetPresenterEnabled?.Invoke() ?? true);
                if (item.Tag as string == "subtitle")
                    PaintToggle(item, "Canlı altyazı", GetSubtitleEnabled?.Invoke() ?? false);
            }
        };
        return menu;
    }

    private MenuItem PresenterToggle()
    {
        var item = new MenuItem
        {
            Header = "Sunum araçları",
            Icon = Glyph(0xE70F),
            Tag = "presenter",
        };
        PaintToggle(item, "Sunum araçları", GetPresenterEnabled?.Invoke() ?? true);
        item.Click += (_, _) =>
        {
            bool next = !(GetPresenterEnabled?.Invoke() ?? true);
            PresenterEnabledChanged?.Invoke(next);
            PaintToggle(item, "Sunum araçları", next);
        };
        return item;
    }

    private MenuItem SubtitleToggle()
    {
        var item = new MenuItem
        {
            Header = "Canlı altyazı",
            Icon = Glyph(0xE7F0),
            Tag = "subtitle",
        };
        PaintToggle(item, "Canlı altyazı", GetSubtitleEnabled?.Invoke() ?? false);
        item.Click += (_, _) =>
        {
            bool next = !(GetSubtitleEnabled?.Invoke() ?? false);
            SubtitleEnabledChanged?.Invoke(next);
            PaintToggle(item, "Canlı altyazı", next);
        };
        return item;
    }

    public void SetSubtitleChecked(bool on)
    {
        if (_icon.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag as string == "subtitle") is { } item)
            PaintToggle(item, "Canlı altyazı", on);
    }

    private static MenuItem MenuItem(string header, Action action, int glyph, bool isDefault = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = Glyph(glyph),
            FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void PaintToggle(MenuItem item, string title, bool on)
    {
        item.Header = on ? title + "  ✓" : title;
    }

    private static TextBlock Glyph(int code) => new()
    {
        Text = char.ConvertFromUtf32(code),
        FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
        FontSize = 13,
        Width = 16,
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6)),
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Tepsi balonu / kısa bildirim.</summary>
    public void ShowMessage(string title, string message)
    {
        try
        {
            _icon.ShowNotification(title, message);
        }
        catch
        {
            // Bildirim başarısızsa yut.
        }
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}

/// <summary>Basit, bağımlılıksız ICommand — tray sol tık için.</summary>
internal sealed class RelayCommandLite : System.Windows.Input.ICommand
{
    private readonly Action _action;
    public RelayCommandLite(Action action) => _action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
}
