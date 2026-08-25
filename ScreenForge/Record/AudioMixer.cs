using System.Collections.Concurrent;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenForge.Record;

internal sealed class AudioMixer : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BytesPerFrame = 4;

    private readonly ConcurrentQueue<byte[]> _pcm = new();
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _loop;
    private WasapiCapture? _mic;
    private MMDevice? _micDevice;
    private WaveFormat? _loopFormat;
    private WaveFormat? _micFormat;
    private float[] _sysRemain = [];
    private float[] _micRemain = [];
    private volatile float _sysPeak;
    private volatile float _micPeak;
    private volatile bool _paused;
    private volatile bool _enqueue;
    private bool _disposed;

    public AudioMixer(bool system, bool microphone, string? micDeviceId)
    {
        HasSystem = system;
        HasMic = microphone;
        if (system)
            StartLoopback();
        if (microphone)
            StartMic(micDeviceId);
    }

    public bool HasSystem { get; }
    public bool HasMic { get; }
    public bool HasAudio => _loop != null || _mic != null;
    public float SystemPeak => _sysPeak;
    public float MicPeak => _micPeak;

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public bool Enqueue
    {
        get => _enqueue;
        set => _enqueue = value;
    }

    public void SetMicrophone(string? deviceId)
    {
        lock (_gate)
        {
            StopMic();
            if (HasMic)
                StartMic(deviceId);
        }
    }

    public bool TryDequeue(out byte[] pcm) => _pcm.TryDequeue(out pcm!);

    public void FlushRemainder()
    {
        lock (_gate)
            DrainMix(force: true);
    }

    private void StartLoopback()
    {
        try
        {
            _loop = new WasapiLoopbackCapture();
            _loopFormat = _loop.WaveFormat;
            var format = _loopFormat;
            _loop.DataAvailable += (_, e) => OnData(e, isMic: false, format);
            _loop.StartRecording();
        }
        catch
        {
            _loop?.Dispose();
            _loop = null;
            _loopFormat = null;
        }
    }

    private void StartMic(string? deviceId)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                try { device = enumerator.GetDevice(deviceId); }
                catch { device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
            }
            else
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }

            _micDevice = device;
            _mic = new WasapiCapture(device, true, 50);
            _micFormat = _mic.WaveFormat;
            var format = _micFormat;
            _mic.DataAvailable += (_, e) => OnData(e, isMic: true, format);
            _mic.StartRecording();
        }
        catch
        {
            StopMic();
        }
    }

    private void OnData(WaveInEventArgs e, bool isMic, WaveFormat? format)
    {
        if (_disposed || e.BytesRecorded <= 0 || format == null)
            return;

        var samples = ToStereo48k(e.Buffer, e.BytesRecorded, format, out float peak);
        if (samples.Length == 0)
            return;

        if (isMic) _micPeak = peak;
        else _sysPeak = peak;

        if (_paused || !_enqueue)
            return;

        lock (_gate)
        {
            if (isMic) _micRemain = Concat(_micRemain, samples);
            else _sysRemain = Concat(_sysRemain, samples);
            DrainMix(force: false);
        }
    }

    private void DrainMix(bool force)
    {
        int sysFrames = _sysRemain.Length / Channels;
        int micFrames = _micRemain.Length / Channels;
        bool both = _loop != null && _mic != null;
        int frames = both ? Math.Max(sysFrames, micFrames) : (_loop != null ? sysFrames : micFrames);

        const int minFrames = SampleRate / 50;
        if (!force && frames < minFrames)
            return;
        if (frames <= 0)
            return;

        var pcm = new byte[frames * BytesPerFrame];
        for (int i = 0; i < frames; i++)
        {
            float l = 0, r = 0;
            if (i < sysFrames)
            {
                l += _sysRemain[i * 2];
                r += _sysRemain[i * 2 + 1];
            }
            if (i < micFrames)
            {
                l += _micRemain[i * 2];
                r += _micRemain[i * 2 + 1];
            }
            WritePcm16(pcm, i * 4, l, r);
        }

        _sysRemain = sysFrames > frames ? _sysRemain.AsSpan(frames * Channels).ToArray() : [];
        _micRemain = micFrames > frames ? _micRemain.AsSpan(frames * Channels).ToArray() : [];
        _pcm.Enqueue(pcm);
    }

    private static void WritePcm16(byte[] dest, int offset, float l, float r)
    {
        short sl = (short)Math.Clamp((int)(l * 32767f), short.MinValue, short.MaxValue);
        short sr = (short)Math.Clamp((int)(r * 32767f), short.MinValue, short.MaxValue);
        dest[offset] = (byte)sl;
        dest[offset + 1] = (byte)(sl >> 8);
        dest[offset + 2] = (byte)sr;
        dest[offset + 3] = (byte)(sr >> 8);
    }

    private static float[] Concat(float[] a, float[] b)
    {
        if (a.Length == 0) return b;
        var n = new float[a.Length + b.Length];
        a.CopyTo(n, 0);
        b.CopyTo(n, a.Length);
        return n;
    }

    private static float[] ToStereo48k(byte[] buffer, int bytes, WaveFormat format, out float peak)
    {
        peak = 0;
        float[] interleaved;
        try
        {
            using var ms = new MemoryStream(buffer, 0, bytes, writable: false);
            using var raw = new RawSourceWaveStream(ms, format);
            ISampleProvider samples = raw.ToSampleProvider();
            if (samples.WaveFormat.Channels == 1)
                samples = new MonoToStereoSampleProvider(samples);

            int srcRate = samples.WaveFormat.SampleRate;
            int srcCh = samples.WaveFormat.Channels;
            var scratch = new float[Math.Max(srcCh, bytes)];
            int n = samples.Read(scratch, 0, scratch.Length);
            if (n <= 0) return [];

            int srcFrames = n / srcCh;
            interleaved = new float[srcFrames * 2];
            for (int i = 0; i < srcFrames; i++)
            {
                float l = scratch[i * srcCh];
                float r = srcCh > 1 ? scratch[i * srcCh + 1] : l;
                interleaved[i * 2] = l;
                interleaved[i * 2 + 1] = r;
                peak = Math.Max(peak, Math.Max(Math.Abs(l), Math.Abs(r)));
            }

            if (srcRate == SampleRate)
                return interleaved;

            int dstFrames = Math.Max(1, (int)((long)srcFrames * SampleRate / srcRate));
            var dst = new float[dstFrames * 2];
            for (int i = 0; i < dstFrames; i++)
            {
                int i0 = Math.Min(srcFrames - 1, (int)(i * (srcRate / (double)SampleRate)));
                dst[i * 2] = interleaved[i0 * 2];
                dst[i * 2 + 1] = interleaved[i0 * 2 + 1];
            }
            return dst;
        }
        catch
        {
            return [];
        }
    }

    private void StopMic()
    {
        try { _mic?.StopRecording(); } catch { /* ignore */ }
        _mic?.Dispose();
        _mic = null;
        _micDevice?.Dispose();
        _micDevice = null;
        _micFormat = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _loop?.StopRecording(); } catch { /* ignore */ }
        _loop?.Dispose();
        _loop = null;
        StopMic();
        while (_pcm.TryDequeue(out _)) { }
    }
}
