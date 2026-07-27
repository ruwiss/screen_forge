using System.Runtime.CompilerServices;
using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// GIF üzerine çizilen nesnelerin katmanı.
/// </summary>
/// <remarks>
/// Katman tek bir <see cref="Editor.Scene"/> tutar; her nesnenin hangi
/// karelerde görüneceği ve konumu <b>kendine aittir</b>. Arka plan atanmaz;
/// kare pikselleri ayrı tutulur ve kompozit sırasında birleştirilir.
/// </remarks>
public sealed class AnnotationTrack
{
    // Nesne kimliği yerine referans anahtarı: nesne atıldığında klibi de
    // otomatik toplanır, elle temizlik gerekmez.
    private readonly ConditionalWeakTable<SceneItem, ObjectClip> _clips = new();

    // Tür başına sayaç: "Dikdörtgen 1", "Dikdörtgen 2"… Silinen adlar geri
    // kullanılmaz ki aynı ad iki kez görünmesin.
    private readonly Dictionary<string, int> _nameCounters = new();

    private int _colorIndex;

    public AnnotationTrack(SKSize canvasSize)
    {
        Scene = new Scene { CanvasSize = canvasSize };
    }

    /// <summary>Bu katmana ait çizim öğeleri.</summary>
    public Scene Scene { get; }

    public bool IsEmpty => Scene.Items.Count == 0;

    /// <summary>Nesnenin klibini döndürür; yoksa oluşturur.</summary>
    public ObjectClip ClipOf(SceneItem item)
    {
        if (_clips.TryGetValue(item, out var clip))
            return clip;

        // Kayıtsız nesne: tüm kareleri kapsayan varsayılan.
        clip = new ObjectClip(0, int.MaxValue - 1);
        _clips.Add(item, clip);
        return clip;
    }

    /// <summary>Klibi doğrudan atar (kopyalama için).</summary>
    public void SetClip(SceneItem item, ObjectClip clip)
    {
        _clips.Remove(item);
        _clips.Add(item, clip);
    }

    /// <summary>Nesne daha önce kaydedilmiş mi.</summary>
    public bool IsRegistered(SceneItem item) => _clips.TryGetValue(item, out _);

    /// <summary>Yeni eklenen nesneye kare aralığı verir.</summary>
    public ObjectClip Register(SceneItem item, int startFrame, int endFrame)
    {
        var clip = ClipOf(item);
        clip.StartFrame = Math.Max(0, Math.Min(startFrame, endFrame));
        clip.EndFrame = Math.Max(clip.StartFrame, endFrame);
        return clip;
    }

    /// <summary>
    /// Yeni nesneye sıralı ad ve kendine özgü renk atar.
    /// </summary>
    public ObjectClip Register(SceneItem item, int startFrame, int endFrame, string baseName)
    {
        var clip = Register(item, startFrame, endFrame);

        _nameCounters.TryGetValue(baseName, out int used);
        _nameCounters[baseName] = used + 1;

        clip.Name = ObjectPalette.NameFor(baseName, used + 1);
        clip.Color = ObjectPalette.ColorFor(_colorIndex++);
        return clip;
    }

    /// <summary>Kopyalanan nesneye yeni ad verir, rengi korur.</summary>
    public ObjectClip RegisterCopy(SceneItem item, ObjectClip source, string baseName)
    {
        var clip = source.Clone();

        _nameCounters.TryGetValue(baseName, out int used);
        _nameCounters[baseName] = used + 1;
        clip.Name = ObjectPalette.NameFor(baseName, used + 1);

        SetClip(item, clip);
        return clip;
    }

    /// <summary>Verilen karede çizilecek nesneleri z-sırasıyla toplar.</summary>
    public List<SceneItem> ItemsAt(int frame)
    {
        var visible = new List<SceneItem>();

        foreach (var item in Scene.Items)
        {
            if (ClipOf(item).CoversFrame(frame))
                visible.Add(item);
        }

        return visible;
    }

    /// <summary>Herhangi bir karede çizilecek nesne var mı.</summary>
    public bool HasVisibleItems()
    {
        foreach (var item in Scene.Items)
        {
            if (ClipOf(item).Visible)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Verilen karedeki nesneleri temizler.
    /// </summary>
    /// <remarks>
    /// Nesne yalnızca o karede görünüyorsa tümüyle silinir; daha geniş bir
    /// aralığı kapsıyorsa aralık kısaltılır ve nesne diğer karelerde kalır.
    /// </remarks>
    public int ClearFrame(int frame)
    {
        var doomed = new List<SceneItem>();
        int changed = 0;

        foreach (var item in ItemsAt(frame))
        {
            var clip = ClipOf(item);

            if (clip.StartFrame == frame && clip.EndFrame == frame)
            {
                doomed.Add(item);
            }
            else if (frame == clip.StartFrame)
            {
                clip.StartFrame = frame + 1;
            }
            else if (frame == clip.EndFrame)
            {
                clip.EndFrame = frame - 1;
            }
            else
            {
                // Ortadan kesme: nesne bu kareden sonra görünmesin.
                clip.EndFrame = frame - 1;
            }

            changed++;
        }

        foreach (var item in doomed)
            Scene.Items.Remove(item);

        return changed;
    }

    public void Clamp(int frameCount)
    {
        foreach (var item in Scene.Items)
            ClipOf(item).Clamp(frameCount);
    }

    public void ShiftForFrameChange(int at, int delta)
    {
        foreach (var item in Scene.Items)
            ClipOf(item).ShiftForFrameChange(at, delta);
    }
}
