namespace ScreenForge.Record;

internal interface IFrameSource : IDisposable
{
    bool TryCopyBgra(Span<byte> dest, out long qpcHundredNanos);
}
