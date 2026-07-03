using System.Threading;
using Velopack;
using Velopack.Sources;

namespace ScreenForge.Updates;

public static class AutoUpdateService
{
    private const string ReleasesUrl = "https://github.com/ruwiss/screen_forge";
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

            var manager = new UpdateManager(new GithubSource(ReleasesUrl, null, prerelease: false));
            if (!manager.IsInstalled)
                return;

            if (manager.UpdatePendingRestart is { } pending)
            {
                notify("Güncelleme hazır", "ScreenForge güncellemesi uygulanıyor.");
                await ApplyAndRestartAsync(manager, pending, shutdown);
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update == null)
                return;

            notify("Güncelleme bulundu", "Yeni ScreenForge sürümü indiriliyor.");
            await manager.DownloadUpdatesAsync(update);

            notify("Güncelleme hazır", "ScreenForge yeniden başlatılıp güncellenecek.");
            await ApplyAndRestartAsync(manager, update.TargetFullRelease, shutdown);
        }
        catch
        {
            // Güncelleme hatası ana uygulamayı etkilememeli.
        }
    }

    private static async Task ApplyAndRestartAsync(UpdateManager manager, VelopackAsset release, Action shutdown)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        manager.WaitExitThenApplyUpdates(release, silent: true, restart: true);
        shutdown();
    }
}
