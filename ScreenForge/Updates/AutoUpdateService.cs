using System.IO;
using System.Threading;
using Velopack;
using Velopack.Sources;

namespace ScreenForge.Updates;

public static class AutoUpdateService
{
    private const string ReleasesUrl = "https://github.com/ruwiss/screen_forge/releases/latest/download";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenForge",
        "update.log");
    private static int _started;

    public static void CheckOnStartup(Action<string, string> notify, Action shutdown)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = CheckAndApplyAsync(notify, shutdown);
    }

    private static async Task CheckAndApplyAsync(Action<string, string> notify, Action shutdown)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            WriteLog($"Kontrol başladı. ProcessPath={Environment.ProcessPath}; BaseDirectory={AppContext.BaseDirectory}");

            var manager = new UpdateManager(new SimpleWebSource(ReleasesUrl));
            var isInstalled = manager.IsInstalled;
            WriteLog($"Velopack durumu. IsInstalled={isInstalled}");

            if (!isInstalled)
            {
                WriteLog("Güncelleme pas geçildi. Uygulama Velopack kurulumundan çalışmıyor.");
                return;
            }

            WriteLog($"Kurulu sürüm. CurrentVersion={manager.CurrentVersion}; PendingRestart={manager.UpdatePendingRestart != null}");

            if (manager.UpdatePendingRestart is { } pending)
            {
                WriteLog($"Bekleyen güncelleme uygulanacak. Version={pending.Version}");
                // Ara bildirim yok — yalnızca tek final bildirim.
                notify("Uygulama güncellendi", $"ScreenForge {pending.Version} yüklendi.");
                await ApplyAndRestartAsync(manager, pending, shutdown);
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update == null)
            {
                WriteLog("Yeni güncelleme bulunamadı.");
                return;
            }

            WriteLog($"Güncelleme bulundu. TargetVersion={update.TargetFullRelease.Version}");
            // İndirme/hazırlık sessiz — kullanıcıyı spam'leme.
            await manager.DownloadUpdatesAsync(update);

            WriteLog("Güncelleme indirildi. Yeniden başlatma hazırlanıyor.");
            notify("Uygulama güncellendi",
                $"ScreenForge {update.TargetFullRelease.Version} yüklendi.");
            await ApplyAndRestartAsync(manager, update.TargetFullRelease, shutdown);
        }
        catch (Exception ex)
        {
            WriteLog("Güncelleme hatası: " + ex);
            // Güncelleme hatası ana uygulamayı etkilememeli.
        }
    }

    private static async Task ApplyAndRestartAsync(UpdateManager manager, VelopackAsset release, Action shutdown)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        manager.WaitExitThenApplyUpdates(release, silent: true, restart: true);
        shutdown();
    }

    private static void WriteLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Log yazılamazsa güncelleme akışı etkilenmemeli.
        }
    }
}
