using System.IO;
using System.Threading;
using System.Windows;
using ScreenForge.Hotkeys;
using ScreenForge.Settings;
using ScreenForge.Tray;

namespace ScreenForge;

public partial class App : Application
{
    private const string MutexName = "ScreenForge_SingleInstance_Mutex_8e0f7a12";
    private Mutex? _instanceMutex;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- Tek örnek kontrolü ----
        _instanceMutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("ScreenForge zaten çalışıyor. Sistem tepsisini kontrol edin.",
                "ScreenForge", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        LoadEnvFile();

        Settings = AppSettings.Load(out bool isFirstRun);
        ScreenForge.Settings.StartupManager.SetEnabled(Settings.LaunchAtStartup);

        // ---- Tepsi ikonu ----
        _tray = new TrayIconService();
        _tray.CaptureRegionRequested += OnCaptureRegion;
        _tray.CaptureFullScreenRequested += OnCaptureFullScreen;
        _tray.CollageRequested += OnCollage;
        _tray.ColorPickerRequested += OnColorPicker;
        _tray.TrayUploadRequested += OnTrayUpload;
        _tray.SettingsRequested += OnSettings;
        _tray.AboutRequested += OnAbout;
        _tray.ExitRequested += OnExit;

        // ---- Global kısayollar ----
        _hotkeys = new HotkeyService();
        RegisterHotkeys();

        // İlk açılışta bölge yakalama kısayolunu bildir
        if (isFirstRun)
        {
            var regionKey = Settings.RegionHotkey.ToString();
            _tray.ShowMessage("ScreenForge'a Hoş Geldiniz",
                $"Bölge yakalamak için {regionKey} kısayolunu kullanabilirsiniz.");
        }
    }

    /// <summary>Ayarlardaki tüm kısayolları (yeniden) kaydeder.</summary>
    public void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        _hotkeys.Register(Settings.RegionHotkey, OnCaptureRegion, "Bölge yakalama");
        _hotkeys.Register(Settings.FullScreenHotkey, OnCaptureFullScreen, "Tam ekran yakalama");
        _hotkeys.Register(Settings.FullScreenUploadHotkey, OnCaptureFullScreenUpload, "Tam ekran anında yükleme");
        _hotkeys.Register(Settings.CollageHotkey, OnCollage, "Kolaj / yerleştirme");

        if (_hotkeys.FailedRegistrations.Count > 0)
        {
            _tray?.ShowMessage("Kısayol uyarısı",
                "Bazı kısayollar kaydedilemedi (başka uygulamada kullanılıyor olabilir):\n" +
                string.Join("\n", _hotkeys.FailedRegistrations));
        }
    }

    private bool _captureInProgress;

    // Serbest mod sahnesi — oturum boyunca bellekte hatırlanır.
    private Editor.Scene? _freeScene;

    /// <summary>System.Drawing.Bitmap → SKBitmap (sahne arka planı için).</summary>
    private static SkiaSharp.SKBitmap ToSkBitmap(System.Drawing.Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var info = new SkiaSharp.SKImageInfo(bmp.Width, bmp.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
            var sk = new SkiaSharp.SKBitmap(info);
            unsafe
            {
                Buffer.MemoryCopy((void*)data.Scan0, (void*)sk.GetPixels(), sk.ByteCount, data.Stride * bmp.Height);
            }
            return sk;
        }
        finally { bmp.UnlockBits(data); }
    }

    private void OnCaptureRegion() => OpenCaptureOverlay(ScreenForge.Windows.CaptureMode.Region);

    private void OnCaptureFullScreen() => OpenCaptureOverlay(ScreenForge.Windows.CaptureMode.FullScreen);

    private void OnCollage() => OpenCaptureOverlay(ScreenForge.Windows.CaptureMode.Free);

    private void OnColorPicker()
    {
        var picker = new Windows.ColorPickerOverlayWindow();
        picker.Show();
    }

    private void OnTrayUpload()
    {
        var uploader = new Windows.FileDropUploadWindow();
        uploader.Show();
    }

    /// <summary>
    /// Lightshot tarzı birleşik ekran-üstü yakalama katmanını açar. Mod seçim çubuğundan da
    /// değiştirilebilir. Ayrı editör penceresi açılmaz.
    /// </summary>
    private void OpenCaptureOverlay(ScreenForge.Windows.CaptureMode mode)
    {
        if (_captureInProgress) return;
        _captureInProgress = true;
        try
        {
            var bounds = Capture.ScreenCapture.VirtualScreenBounds;
            // Overlay görüntünün sahibi olur; kapanınca dispose eder.
            var full = Capture.ScreenCapture.CaptureVirtualScreen(Settings.ShowCursor);
            var overlay = new ScreenForge.Windows.CaptureOverlayWindow(full, bounds, Settings, mode);
            // Serbest sahne oturum boyunca bellekte kalır (Esc sonrası korunur).
            overlay.FreeSceneProvider = () => _freeScene;
            overlay.FreeSceneSink = s => _freeScene = s;
            overlay.Closed += (_, _) => { full.Dispose(); _captureInProgress = false; };
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            _captureInProgress = false;
            _tray?.ShowMessage("Hata", "Yakalama başarısız: " + ex.Message);
        }
    }

    private async void OnCaptureFullScreenUpload()
    {
        if (_captureInProgress) return;
        _captureInProgress = true;
        try
        {
            byte[] bytes;
            string mime = "image/png";
            using (var full = Capture.ScreenCapture.CaptureVirtualScreen(Settings.ShowCursor))
            using (var sk = ToSkBitmap(full))
            using (var data = Editor.ImageExporter.Encode(sk, ScreenForge.Settings.ImageFormat.Png, 100))
                bytes = data.ToArray();

            var toast = new Windows.UploadToastWindow();
            toast.Show();

            Upload.IUploadProvider provider = new Upload.PrntscrUploadProvider();
            var result = await provider.UploadAsync(bytes, mime, toast.ReportProgress);

            toast.ShowResult(result.Url);
            if (Settings.AutoCopyLinkAfterUpload) { Clipboard.SetText(result.Url); toast.SetCopied(); }
            if (Settings.AutoCloseUploadWindow) toast.AutoCloseSoon();
        }
        catch (Exception ex)
        {
            _tray?.ShowMessage("Yükleme hatası", ex.Message);
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private Windows.SettingsWindow? _settingsWindow;
    private Windows.AboutWindow? _aboutWindow;

    private void OnSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _hotkeys?.UnregisterAll();
        _settingsWindow = new Windows.SettingsWindow(Settings, () => { });
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; RegisterHotkeys(); };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnAbout()
    {
        if (_aboutWindow != null) { _aboutWindow.Activate(); return; }
        _aboutWindow = new Windows.AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void OnExit()
    {
        Shutdown();
    }

    private static void LoadEnvFile()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = trimmed[..eq].Trim();
                    var val = trimmed[(eq + 1)..].Trim();
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        Environment.SetEnvironmentVariable(key, val);
                }
                return;
            }
            dir = Path.GetDirectoryName(dir);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
