using System.Runtime.InteropServices;

namespace ScreenForge.Presenter;

public sealed class Magnifier : IDisposable
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private bool _ready;
    private bool _disposed;
    private float _lastMag = 1f;
    private int _lastOx = int.MinValue;
    private int _lastOy;

    public bool TryInitialize()
    {
        if (_ready) return true;
        try
        {
            if (!MagInitialize())
                return false;
            _ready = true;
            _lastMag = 1f;
            _lastOx = int.MinValue;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Apply(float mag, int xOffset, int yOffset)
    {
        mag = Math.Clamp(mag, 1f, 8f);
        if (mag <= 1.001f)
        {
            ClearTransform();
            return true;
        }

        if (!TryInitialize())
            return false;

        int pw = Math.Max(1, GetSystemMetrics(SmCxScreen));
        int ph = Math.Max(1, GetSystemMetrics(SmCyScreen));
        ZoomMath.ClampOffsets(mag, 0, 0, pw, ph, ref xOffset, ref yOffset);
        xOffset = Math.Max(0, xOffset);
        yOffset = Math.Max(0, yOffset);

        if (Math.Abs(mag - _lastMag) < 0.001f && xOffset == _lastOx && yOffset == _lastOy)
            return true;

        try
        {
            if (!MagSetFullscreenTransform(mag, xOffset, yOffset))
                return false;
        }
        catch
        {
            return false;
        }

        _lastMag = mag;
        _lastOx = xOffset;
        _lastOy = yOffset;
        return true;
    }

    public void ClearTransform()
    {
        if (!_ready) return;
        try { MagSetFullscreenTransform(1f, 0, 0); } catch { }
        _lastMag = 1f;
        _lastOx = int.MinValue;
        _lastOy = 0;
    }

    public void Reset() => ClearTransform();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearTransform();
        if (!_ready) return;
        try { MagUninitialize(); } catch { }
        _ready = false;
    }
}
