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
    public event Action? SettingsRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;

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

        menu.Items.Add(MenuItem("Bölge Yakala", () => CaptureRegionRequested?.Invoke(), isDefault: true));
        menu.Items.Add(MenuItem("Tam Ekran Yakala", () => CaptureFullScreenRequested?.Invoke()));
        menu.Items.Add(MenuItem("Serbest / Yerleştirme", () => CollageRequested?.Invoke()));
        menu.Items.Add(MenuItem("Renk Seçici", () => ColorPickerRequested?.Invoke()));
        menu.Items.Add(MenuItem("Yükleme Yap", () => TrayUploadRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Ayarlar", () => SettingsRequested?.Invoke()));
        menu.Items.Add(MenuItem("Hakkında", () => AboutRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Çıkış", () => ExitRequested?.Invoke()));

        return menu;
    }

    private static MenuItem MenuItem(string header, Action action, bool isDefault = false)
    {
        var item = new MenuItem
        {
            Header = header,
            FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
        };
        item.Click += (_, _) => action();
        return item;
    }

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
