namespace ScreenForge.Record;

public interface IRecordingSession : IDisposable
{
    TimeSpan Elapsed { get; }
    int Fps { get; }
    int FrameCount { get; }
    double CaptureEfficiency { get; }
    bool IsPaused { get; }
    event Action? StateChanged;
    event Action? LimitReached;
    void Start();
    void Pause();
    void Resume();
    void Stop();
    /// <summary>Toplanan kareleri ve temp dosyayı siler, kaydı baştan başlatır.</summary>
    void Restart();
}

public enum RecordingKind { Gif, Video }
