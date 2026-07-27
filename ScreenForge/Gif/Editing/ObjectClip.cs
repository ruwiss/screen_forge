using SkiaSharp;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Bir çizim nesnesinin karelerdeki ömrü.
/// </summary>
/// <remarks>
/// Nesne <see cref="StartFrame"/>–<see cref="EndFrame"/> arasında görünür.
/// Kullanıcı bir karede nesneyi taşıdığında o kareye otomatik olarak konum
/// anahtarı yazılır; ayrı bir "animasyonu aç" adımı yoktur. İki anahtar
/// arasındaki karelerde konum yumuşak geçişle hesaplanır.
/// </remarks>
public sealed class ObjectClip
{
    private readonly List<(int Frame, SKPoint Offset)> _keys = new();

    public ObjectClip(int startFrame, int endFrame)
    {
        StartFrame = Math.Max(0, Math.Min(startFrame, endFrame));
        EndFrame = Math.Max(StartFrame, endFrame);
    }

    /// <summary>Nesnenin göründüğü ilk kare (dahil).</summary>
    public int StartFrame { get; set; }

    /// <summary>Nesnenin göründüğü son kare (dahil).</summary>
    public int EndFrame { get; set; }

    /// <summary>Kapalıysa nesne hiçbir karede çizilmez.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Şeritte görünen ad, örn. "Dikdörtgen 2".</summary>
    public string Name { get; set; } = "Nesne";

    /// <summary>Şerit çubuğunun rengi; nesneleri ayırt etmeye yarar.</summary>
    public SKColor Color { get; set; } = new(0xEA, 0x6F, 0x12);

    public int Length => EndFrame - StartFrame + 1;

    /// <summary>Konum anahtarları, kare sırasına göre.</summary>
    public IReadOnlyList<(int Frame, SKPoint Offset)> Keys => _keys;

    /// <summary>Nesne birden çok konumda duruyor mu (hareket ediyor mu).</summary>
    public bool IsMoving => _keys.Count > 1;

    public bool CoversFrame(int frame)
        => Visible && frame >= StartFrame && frame <= EndFrame;

    public bool HasKeyAt(int frame)
    {
        foreach (var key in _keys)
        {
            if (key.Frame == frame)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Nesnenin verilen karedeki konum sapmasını yazar.
    /// </summary>
    /// <remarks>
    /// İlk anahtar yazıldığında nesne o kareye sabitlenir. İkinci bir karede
    /// farklı konum yazılınca aradaki kareler kendiliğinden animasyona döner.
    /// </remarks>
    public void SetOffsetAt(int frame, SKPoint offset)
    {
        for (int i = 0; i < _keys.Count; i++)
        {
            if (_keys[i].Frame != frame)
                continue;

            _keys[i] = (frame, offset);
            return;
        }

        _keys.Add((frame, offset));
        _keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    public bool RemoveKeyAt(int frame) => _keys.RemoveAll(k => k.Frame == frame) > 0;

    public void ClearKeys() => _keys.Clear();

    /// <summary>Verilen karedeki konum sapması.</summary>
    public SKPoint OffsetAt(int frame)
    {
        if (_keys.Count == 0)
            return SKPoint.Empty;

        if (_keys.Count == 1 || frame <= _keys[0].Frame)
            return _keys[0].Offset;

        if (frame >= _keys[^1].Frame)
            return _keys[^1].Offset;

        for (int i = 0; i < _keys.Count - 1; i++)
        {
            var (fromFrame, fromOffset) = _keys[i];
            var (toFrame, toOffset) = _keys[i + 1];

            if (frame < fromFrame || frame > toFrame)
                continue;

            int span = toFrame - fromFrame;
            if (span <= 0)
                return toOffset;

            float t = (frame - fromFrame) / (float)span;
            return new SKPoint(
                fromOffset.X + (toOffset.X - fromOffset.X) * t,
                fromOffset.Y + (toOffset.Y - fromOffset.Y) * t);
        }

        return _keys[^1].Offset;
    }

    /// <summary>Önceki anahtarın karesi; yoksa -1.</summary>
    public int PreviousKeyFrame(int frame)
    {
        int result = -1;

        foreach (var key in _keys)
        {
            if (key.Frame < frame && key.Frame > result)
                result = key.Frame;
        }

        return result;
    }

    /// <summary>Sonraki anahtarın karesi; yoksa -1.</summary>
    public int NextKeyFrame(int frame)
    {
        int result = -1;

        foreach (var key in _keys)
        {
            if (key.Frame > frame && (result < 0 || key.Frame < result))
                result = key.Frame;
        }

        return result;
    }

    /// <summary>Nesneyi tüm karelerde göster.</summary>
    public void ExtendToAll(int frameCount)
    {
        StartFrame = 0;
        EndFrame = Math.Max(0, frameCount - 1);
    }

    /// <summary>
    /// Nesneyi verilen kareden itibaren gizler.
    /// </summary>
    /// <returns>Nesne tümüyle kaldırılmalıysa <see langword="false"/>.</returns>
    public bool HideFrom(int frame)
    {
        if (frame <= StartFrame)
            return false;

        EndFrame = frame - 1;
        return true;
    }

    /// <summary>Nesneyi verilen kareye kadar gizler.</summary>
    /// <returns>Nesne tümüyle kaldırılmalıysa <see langword="false"/>.</returns>
    public bool ShowFrom(int frame)
    {
        if (frame >= EndFrame)
            return false;

        StartFrame = frame;
        return true;
    }

    public void Clamp(int frameCount)
    {
        int last = Math.Max(0, frameCount - 1);
        StartFrame = Math.Clamp(StartFrame, 0, last);
        EndFrame = Math.Clamp(EndFrame, StartFrame, last);
    }

    /// <summary>Kare eklenip silindiğinde aralığı ve anahtarları kaydırır.</summary>
    public void ShiftForFrameChange(int at, int delta)
    {
        if (delta == 0)
            return;

        if (StartFrame >= at) StartFrame = Math.Max(0, StartFrame + delta);
        if (EndFrame >= at) EndFrame = Math.Max(StartFrame, EndFrame + delta);

        for (int i = 0; i < _keys.Count; i++)
        {
            if (_keys[i].Frame < at)
                continue;

            _keys[i] = (Math.Max(0, _keys[i].Frame + delta), _keys[i].Offset);
        }

        _keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    public ObjectClip Clone()
    {
        var copy = new ObjectClip(StartFrame, EndFrame)
        {
            Visible = Visible,
            Name = Name,
            Color = Color,
        };

        copy._keys.AddRange(_keys);
        return copy;
    }
}
