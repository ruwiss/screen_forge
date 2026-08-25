using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace ScreenForge.Record;

internal sealed class MfH264Writer : IDisposable
{
    private readonly IMFSinkWriter _writer;
    private readonly int _streamIndex;
    private readonly int _audioStream;
    private readonly object _writeLock = new();
    private readonly int _width;
    private readonly int _height;
    private readonly long _frameDuration;
    private bool _started;
    private bool _disposed;

    public MfH264Writer(string path, int width, int height, int fps, int bitrate, bool audio = false)
    {
        _width = width;
        _height = height;
        fps = Math.Max(1, fps);
        _frameDuration = 10_000_000L / fps;

        MediaFactory.MFStartup();

        using var attrs = MediaFactory.MFCreateAttributes(4);
        attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true);
        attrs.Set(SinkWriterAttributeKeys.DisableThrottling, true);

        try
        {
            _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attrs);
        }
        catch (Exception ex)
        {
            MediaFactory.MFShutdown();
            throw new InvalidOperationException("Bu cihazda H.264 kodlayıcı yok (Media Foundation).", ex);
        }

        using (var outType = MediaFactory.MFCreateMediaType())
        {
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, H264);
            outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            outType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            outType.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            outType.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            _streamIndex = _writer.AddStream(outType);
        }

        using (var inType = MediaFactory.MFCreateMediaType())
        {
            inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            inType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            inType.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            inType.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            try
            {
                _writer.SetInputMediaType(_streamIndex, inType, null);
            }
            catch (Exception ex)
            {
                Dispose();
                throw new InvalidOperationException("Bu cihazda H.264 kodlayıcı yok (Media Foundation).", ex);
            }
        }

        _audioStream = audio ? TryAddAudio() : -1;
        _writer.BeginWriting();
        _started = true;
    }

    public bool HasAudio => _audioStream >= 0;

    public void WriteBgra(byte[] bgra, long timestampHundredNanos)
    {
        int stride = _width * 4;
        int byteCount = stride * _height;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(byteCount);
        buffer.Lock(out nint data, out _, out _);
        try
        {
            for (int y = 0; y < _height; y++)
            {
                Marshal.Copy(
                    bgra,
                    y * stride,
                    data + (_height - 1 - y) * stride,
                    stride);
            }
        }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = byteCount;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestampHundredNanos;
        sample.SampleDuration = _frameDuration;
        lock (_writeLock)
            _writer.WriteSample(_streamIndex, sample);
    }

    public void WriteAudio(byte[] pcm, long timestampHundredNanos)
    {
        if (_audioStream < 0 || pcm.Length < 4) return;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(pcm.Length);
        buffer.Lock(out nint data, out _, out _);
        try { Marshal.Copy(pcm, 0, data, pcm.Length); }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = pcm.Length;

        long duration = pcm.Length * 10_000_000L / (AudioMixer.SampleRate * AudioMixer.BytesPerFrame);
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestampHundredNanos;
        sample.SampleDuration = Math.Max(1, duration);
        lock (_writeLock)
            _writer.WriteSample(_audioStream, sample);
    }

    private int TryAddAudio()
    {
        try
        {
            int stream;
            using (var aac = MediaFactory.MFCreateMediaType())
            {
                aac.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                aac.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                aac.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
                aac.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioMixer.SampleRate);
                aac.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                aac.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 16000u);
                aac.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
                stream = _writer.AddStream(aac);
            }

            using (var pcm = MediaFactory.MFCreateMediaType())
            {
                pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
                pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)AudioMixer.SampleRate);
                pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                pcm.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)AudioMixer.BytesPerFrame);
                pcm.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond,
                    (uint)(AudioMixer.SampleRate * AudioMixer.BytesPerFrame));
                _writer.SetInputMediaType(stream, pcm, null);
            }

            return stream;
        }
        catch
        {
            return -1;
        }
    }

    public void Finish()
    {
        if (!_started) return;
        _started = false;
        lock (_writeLock)
            _writer.Finalize();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_started)
                lock (_writeLock)
                    _writer.Finalize();
        }
        catch { /* already finalized or failed */ }
        _writer?.Dispose();
        MediaFactory.MFShutdown();
    }

    private static ulong Pack(int hi, int lo) => ((ulong)(uint)hi << 32) | (uint)lo;

    private static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
}
