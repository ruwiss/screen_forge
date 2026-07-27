using System.Windows;
using System.Windows.Threading;

namespace ScreenForge.Tests;

/// <summary>
/// WPF denetimleri yalnızca STA iş parçacığında oluşturulabilir.
/// Bu yardımcı, test gövdesini STA'da çalıştırır ve uygulama kaynaklarının
/// yüklü olmasını sağlar.
/// </summary>
internal static class WpfRunner
{
    private static readonly object Gate = new();
    private static bool _resourcesLoaded;

    /// <summary>Verilen işi STA iş parçacığında çalıştırır ve hataları yeniden fırlatır.</summary>
    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureResources();
                action();

                // Kapatma/temizleme işlerinin kuyruktan geçmesine izin ver.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        // Pencere kurulumu birkaç saniyeden uzun sürerse bir yerde takılmış demektir.
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("WPF testi zaman aşımına uğradı.");

        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>
    /// Koşul sağlanana kadar mesaj kuyruğunu işler.
    /// Arka planda çalışan düzenlemelerin tamamlanmasını beklemek için kullanılır.
    /// </summary>
    public static void DrainUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (!condition() && clock.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Tema ve ikon sözlüklerini uygulama kaynaklarına yükler.
    /// Pencereler bunlara StaticResource ile başvurur; eksikse oluşturma anında hata verir.
    /// </summary>
    private static void EnsureResources()
    {
        lock (Gate)
        {
            if (Application.Current == null)
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            if (_resourcesLoaded)
                return;

            _resourcesLoaded = true;

            string assembly = typeof(ScreenForge.Gif.GifRecorder).Assembly.GetName().Name!;

            foreach (string path in new[] { "Resources/Theme.xaml", "Resources/Icons.xaml" })
            {
                var uri = new Uri($"pack://application:,,,/{assembly};component/{path}", UriKind.Absolute);
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
        }
    }
}
