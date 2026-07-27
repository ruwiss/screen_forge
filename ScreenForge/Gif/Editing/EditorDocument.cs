using ScreenForge.Gif.Input;

namespace ScreenForge.Gif.Editing;

/// <summary>Tek bir karenin verisi: pikseller, gecikme ve girdi bilgisi.</summary>
public sealed class EditorFrame
{
    /// <summary>BGRA piksel verisi. Kareler arasında paylaşılabilir; asla yerinde değiştirilmez.</summary>
    public required byte[] Pixels { get; init; }

    /// <summary>Karenin ekranda kalma süresi (ms).</summary>
    public required int Delay { get; init; }

    /// <summary>Bu karede olan fare/klavye etkinliği.</summary>
    public required FrameInput Input { get; init; }

    public EditorFrame WithDelay(int delay) => new() { Pixels = Pixels, Delay = delay, Input = Input };

    public EditorFrame WithPixels(byte[] pixels) => new() { Pixels = pixels, Delay = Delay, Input = Input };
}

/// <summary>Düzenlenebilir kare dizisinin değişmez anlık görüntüsü.</summary>
public sealed class EditorSnapshot
{
    public required IReadOnlyList<EditorFrame> Frames { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public int FrameCount => Frames.Count;

    /// <summary>Toplam animasyon süresi.</summary>
    public TimeSpan TotalDuration => TimeSpan.FromMilliseconds(Frames.Sum(f => (long)f.Delay));

    /// <summary>Kare başına ortalama gecikme (ms).</summary>
    public double AverageDelay => Frames.Count == 0 ? 0 : Frames.Average(f => f.Delay);

    /// <summary>Karelerin kapladığı ham bellek.</summary>
    public long ByteSize => Frames.Sum(f => (long)f.Pixels.Length);
}

/// <summary>
/// Düzenleme oturumunun durumu ve geri alma geçmişi.
/// </summary>
/// <remarks>
/// Anlık görüntüler kare <b>referanslarını</b> paylaşır; piksel dizileri kopyalanmaz.
/// Bu yüzden bir geri alma adımı yalnızca liste başına birkaç kilobayt tutar.
/// Kare pikselleri değişmez kabul edilir: bir kareyi düzenlemek yeni
/// <see cref="EditorFrame"/> üretir, var olanı değiştirmez.
/// </remarks>
public sealed class EditorDocument
{
    private readonly List<EditorSnapshot> _undo = new();
    private readonly List<EditorSnapshot> _redo = new();
    private readonly List<string> _undoLabels = new();
    private readonly List<string> _redoLabels = new();
    private readonly int _historyLimit;

    private EditorSnapshot _current;

    /// <summary>Belge değiştiğinde tetiklenir (düzenleme, geri alma veya yineleme).</summary>
    public event Action? Changed;

    public EditorDocument(IReadOnlyList<EditorFrame> frames, int width, int height, int historyLimit = 50)
    {
        _historyLimit = Math.Max(1, historyLimit);
        _current = new EditorSnapshot { Frames = frames, Width = width, Height = height };
    }

    public EditorSnapshot Current => _current;
    public IReadOnlyList<EditorFrame> Frames => _current.Frames;
    public int FrameCount => _current.Frames.Count;
    public int Width => _current.Width;
    public int Height => _current.Height;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Geri alınacak işlemin adı; yoksa <see langword="null"/>.</summary>
    public string? UndoLabel => _undoLabels.Count > 0 ? _undoLabels[^1] : null;

    /// <summary>Yinelenecek işlemin adı; yoksa <see langword="null"/>.</summary>
    public string? RedoLabel => _redoLabels.Count > 0 ? _redoLabels[^1] : null;

    /// <summary>
    /// Yeni kare listesini uygular ve önceki durumu geri alma yığınına iter.
    /// Liste özdeşse hiçbir şey yapılmaz.
    /// </summary>
    public void Apply(string label, IReadOnlyList<EditorFrame> frames)
        => Apply(label, frames, _current.Width, _current.Height);

    /// <summary>Kare boyutunu da değiştiren düzenlemeler için (kırpma, döndürme, ölçekleme).</summary>
    public void Apply(string label, IReadOnlyList<EditorFrame> frames, int width, int height)
    {
        if (ReferenceEquals(frames, _current.Frames) && width == _current.Width && height == _current.Height)
            return;

        PushUndo(label);
        _current = new EditorSnapshot { Frames = frames, Width = width, Height = height };
        _redo.Clear();
        _redoLabels.Clear();
        Changed?.Invoke();
    }

    private void PushUndo(string label)
    {
        _undo.Add(_current);
        _undoLabels.Add(label);

        // En eski adımı at — geçmiş sınırsız büyümesin.
        if (_undo.Count <= _historyLimit)
            return;

        _undo.RemoveAt(0);
        _undoLabels.RemoveAt(0);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        _redo.Add(_current);
        _redoLabels.Add(_undoLabels[^1]);

        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _undoLabels.RemoveAt(_undoLabels.Count - 1);

        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        _undo.Add(_current);
        _undoLabels.Add(_redoLabels[^1]);

        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _redoLabels.RemoveAt(_redoLabels.Count - 1);

        Changed?.Invoke();
        return true;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _undoLabels.Clear();
        _redoLabels.Clear();
    }

    /// <summary>Seçili kareye kadarki kümülatif süre.</summary>
    public TimeSpan TimeUpTo(int index)
    {
        long total = 0;
        for (int i = 0; i <= index && i < _current.Frames.Count; i++)
            total += _current.Frames[i].Delay;

        return TimeSpan.FromMilliseconds(total);
    }
}
