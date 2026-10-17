using System.Windows.Threading;

namespace ScreenForge.Tests;

/// <summary>
/// CaptureOverlayWindow.ItemMoved crash'inin kök nedeni:
/// Dispatcher.BeginInvoke(PositionOptionBar, DispatcherPriority.Render)
/// → BeginInvoke(Delegate, params object[]) overload'ı seçilir,
/// priority metod argümanı sanılır → TargetParameterCountException.
/// </summary>
public sealed class DispatcherBeginInvokeTests
{
    // PositionOptionBar(WpfRect? monOpt = null) ile aynı şekil: opsiyonel parametre.
    private static void TargetWithOptional(object? opt = null) { }

    [Fact]
    public void MethodGroupAsZeroArgAction_DynamicInvokeWithOneArg_ThrowsParameterCountMismatch()
    {
        // BeginInvoke'ın runtime'da yaptığı: DynamicInvoke(args)
        Delegate zeroArg = new Action(() => TargetWithOptional());
        var ex = Assert.Throws<System.Reflection.TargetParameterCountException>(() =>
            zeroArg.DynamicInvoke(new object())); // "priority" yerine sahte arg
        Assert.Contains("Parameter count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeLambda_DynamicInvoke_ZeroArgs_Succeeds()
    {
        int calls = 0;
        Delegate d = new Action(() => { TargetWithOptional(); calls++; });
        d.DynamicInvoke();
        Assert.Equal(1, calls);
    }

    // Kısayol/overlay gecikme yolu bu iki çağrımı kullanıyor: overload yanlış seçilirse
    // aksiyon hiç çalışmaz (TargetParameterCountException) — kısayol ölü görünür.

    [Fact]
    public void BeginInvoke_PriorityFirst_RunsTheCallback()
    {
        WpfRunner.Run(() =>
        {
            bool ran = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => ran = true));
            WpfRunner.DrainUntil(() => ran, 5000);
            Assert.True(ran);
        });
    }

    [Fact]
    public void BeginInvoke_ActionWithPriorityArgument_RunsTheCallback()
    {
        WpfRunner.Run(() =>
        {
            bool ran = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(
                new Action(() => ran = true), DispatcherPriority.Background);
            WpfRunner.DrainUntil(() => ran, 5000);
            Assert.True(ran);
        });
    }
}
