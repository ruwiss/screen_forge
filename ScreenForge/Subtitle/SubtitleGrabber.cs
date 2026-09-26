using System.Drawing;
using ScreenForge.Record;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleGrabber : IDisposable
{
    private IFrameSource? _source;
    private Rectangle _region;
    private byte[] _buffer = [];
    private int _misses;
    private bool _forceGdi;

    public byte[]? Grab(Rectangle region)
    {
        if (region.Width < 2 || region.Height < 2)
            return null;

        if (_source == null || _region != region)
            Recreate(region);

        if (_source == null)
            return null;

        if (_source.TryCopyBgra(_buffer, out _))
        {
            _misses = 0;
            return _buffer;
        }

        if (++_misses < 3)
            return null;

        _misses = 0;
        _forceGdi = _source is DxgiFrameSource;
        Recreate(region);
        if (_source != null && _source.TryCopyBgra(_buffer, out _))
            return _buffer;

        return null;
    }

    private void Recreate(Rectangle region)
    {
        _source?.Dispose();
        _source = null;
        _region = region;
        _buffer = new byte[region.Width * region.Height * 4];
        if (!_forceGdi)
            _source = DxgiFrameSource.TryCreate(region);
        _source ??= new GdiFrameSource(region);
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }
}
