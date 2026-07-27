using System.Runtime.InteropServices;

namespace ScreenForge.Gif;

/// <summary>
/// Sistem zamanlayıcı çözünürlüğünü geçici olarak yükseltir.
/// </summary>
/// <remarks>
/// Windows'un varsayılan zamanlayıcı adımı 15.6 ms'dir. Bu adımda
/// <see cref="Thread.Sleep(int)"/> istenenden çok daha uzun uyuyabilir; 30 fps
/// için gereken 33 ms'lik aralık tutturulamaz ve kareler kaçar.
/// Çözünürlük süreç genelinde etkilidir, bu yüzden yalnızca kayıt süresince
/// açık tutulur ve <see cref="Dispose"/> ile geri alınır.
/// </remarks>
internal sealed class TimerResolution : IDisposable
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint BeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint EndPeriod(uint milliseconds);

    private const uint TIMERR_NOERROR = 0;

    private readonly uint _period;
    private bool _disposed;

    /// <summary>İstenen çözünürlük gerçekten uygulandı mı.</summary>
    public bool Applied { get; }

    public TimerResolution(uint milliseconds)
    {
        _period = Math.Clamp(milliseconds, 1, 15);

        try
        {
            Applied = BeginPeriod(_period) == TIMERR_NOERROR;
        }
        catch (DllNotFoundException)
        {
            // winmm yoksa varsayılan çözünürlükle devam edilir.
            Applied = false;
        }
    }

    public void Dispose()
    {
        if (_disposed || !Applied)
            return;

        _disposed = true;

        try
        {
            EndPeriod(_period);
        }
        catch (DllNotFoundException)
        {
            // Kurulum başarısızsa geri alınacak bir şey de yoktur.
        }
    }
}
