using ScreenForge.Gif.Input;

namespace ScreenForge.Gif.Editing;

/// <summary>Kare silinirken gecikmesine ne olacağı.</summary>
public enum DelayMergeMode
{
    /// <summary>Gecikme atılır; animasyon kısalır.</summary>
    Discard,

    /// <summary>Silinen karenin gecikmesi bir öncekine eklenir; toplam süre korunur.</summary>
    AddToPrevious,

    /// <summary>Silinen gecikmeler kalan karelere eşit dağıtılır; toplam süre korunur.</summary>
    Distribute,
}

/// <summary>Yinelenen kare çiftinde hangisinin atılacağı.</summary>
public enum DuplicateRemoval
{
    /// <summary>Çiftin ilkini at.</summary>
    First,

    /// <summary>Çiftin ikincisini at.</summary>
    Last,
}

/// <summary>
/// Kare dizisi üzerinde çalışan saf dönüşümler.
/// Hepsi yeni liste döndürür; girdi listesi değiştirilmez.
/// </summary>
public static class FrameOperations
{
    /// <summary>Tek karenin taşıyabileceği en uzun gecikme (GIF alanı 16 bit, 1/100 sn).</summary>
    private const int MaxDelayMs = 65535 * 10;

    private const int MinDelayMs = 10;

    // ─── Silme ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verilen indeksleri siler. En az bir kare kalır.
    /// </summary>
    public static List<EditorFrame> Remove(IReadOnlyList<EditorFrame> frames,
        IEnumerable<int> indexes, DelayMergeMode delayMode = DelayMergeMode.AddToPrevious)
    {
        var toRemove = new HashSet<int>(indexes.Where(i => i >= 0 && i < frames.Count));
        if (toRemove.Count == 0 || toRemove.Count >= frames.Count)
            return frames.ToList();

        var result = new List<EditorFrame>(frames.Count - toRemove.Count);
        int carried = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            if (toRemove.Contains(i))
            {
                if (delayMode != DelayMergeMode.Discard)
                    carried += frames[i].Delay;
                continue;
            }

            var frame = frames[i];

            // Biriken gecikmeyi ilk uygun komşuya aktar.
            if (carried > 0 && delayMode == DelayMergeMode.AddToPrevious)
            {
                if (result.Count > 0)
                {
                    result[^1] = result[^1].WithDelay(ClampDelay(result[^1].Delay + carried));
                    carried = 0;
                }
                else
                {
                    // Baştaki kareler silindi; önceki yok, sonrakine ekle.
                    frame = frame.WithDelay(ClampDelay(frame.Delay + carried));
                    carried = 0;
                }
            }

            result.Add(frame);
        }

        if (carried > 0 && result.Count > 0 && delayMode == DelayMergeMode.AddToPrevious)
            result[^1] = result[^1].WithDelay(ClampDelay(result[^1].Delay + carried));

        if (delayMode == DelayMergeMode.Distribute && carried > 0 && result.Count > 0)
            DistributeDelay(result, carried);

        return result;
    }

    /// <summary>Verilen indeksten önceki tüm kareleri siler.</summary>
    public static List<EditorFrame> RemoveBefore(IReadOnlyList<EditorFrame> frames, int index,
        DelayMergeMode delayMode = DelayMergeMode.Discard)
        => index <= 0 ? frames.ToList() : Remove(frames, Enumerable.Range(0, index), delayMode);

    /// <summary>Verilen indeksten sonraki tüm kareleri siler.</summary>
    public static List<EditorFrame> RemoveAfter(IReadOnlyList<EditorFrame> frames, int index,
        DelayMergeMode delayMode = DelayMergeMode.Discard)
        => index >= frames.Count - 1
            ? frames.ToList()
            : Remove(frames, Enumerable.Range(index + 1, frames.Count - index - 1), delayMode);

    /// <summary>Aralığı korur, dışındaki kareleri atar.</summary>
    public static List<EditorFrame> Trim(IReadOnlyList<EditorFrame> frames, int start, int end)
    {
        start = Math.Clamp(start, 0, Math.Max(0, frames.Count - 1));
        end = Math.Clamp(end, start, Math.Max(0, frames.Count - 1));

        var result = new List<EditorFrame>(end - start + 1);
        for (int i = start; i <= end; i++)
            result.Add(frames[i]);

        return result;
    }

    // ─── Çoğaltma ve sıralama ─────────────────────────────────────────────────

    /// <summary>Seçili kareleri hemen ardına kopyalar.</summary>
    public static List<EditorFrame> Duplicate(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes)
    {
        var toCopy = new HashSet<int>(indexes.Where(i => i >= 0 && i < frames.Count));
        if (toCopy.Count == 0)
            return frames.ToList();

        var result = new List<EditorFrame>(frames.Count + toCopy.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            result.Add(frames[i]);

            // Piksel dizisi paylaşılır — kareler değişmez olduğu için güvenli.
            if (toCopy.Contains(i))
                result.Add(frames[i]);
        }

        return result;
    }

    /// <summary>Kare sırasını tersine çevirir.</summary>
    public static List<EditorFrame> Reverse(IReadOnlyList<EditorFrame> frames)
    {
        var result = frames.ToList();
        result.Reverse();
        return result;
    }

    /// <summary>Seçili kareleri bir konum sola taşır.</summary>
    public static List<EditorFrame> MoveLeft(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes)
    {
        var result = frames.ToList();
        var sorted = indexes.Where(i => i > 0 && i < result.Count).Distinct().OrderBy(i => i).ToList();

        foreach (int index in sorted)
            (result[index - 1], result[index]) = (result[index], result[index - 1]);

        return result;
    }

    /// <summary>Seçili kareleri bir konum sağa taşır.</summary>
    public static List<EditorFrame> MoveRight(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes)
    {
        var result = frames.ToList();
        var sorted = indexes.Where(i => i >= 0 && i < result.Count - 1).Distinct().OrderByDescending(i => i).ToList();

        foreach (int index in sorted)
            (result[index + 1], result[index]) = (result[index], result[index + 1]);

        return result;
    }

    // ─── Gecikme ──────────────────────────────────────────────────────────────

    /// <summary>Seçili karelerin gecikmesini sabit değere ayarlar.</summary>
    public static List<EditorFrame> SetDelay(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes, int delayMs)
        => MapSelected(frames, indexes, f => f.WithDelay(ClampDelay(delayMs)));

    /// <summary>Seçili karelerin gecikmesini belirtilen miktarda artırır veya azaltır.</summary>
    public static List<EditorFrame> AdjustDelay(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes, int deltaMs)
        => MapSelected(frames, indexes, f => f.WithDelay(ClampDelay(f.Delay + deltaMs)));

    /// <summary>
    /// Seçili karelerin gecikmesini yüzdeyle ölçekler.
    /// %50 animasyonu iki kat hızlandırır, %200 yavaşlatır.
    /// </summary>
    public static List<EditorFrame> ScaleDelay(IReadOnlyList<EditorFrame> frames, IEnumerable<int> indexes, double percent)
    {
        if (percent <= 0)
            return frames.ToList();

        return MapSelected(frames, indexes,
            f => f.WithDelay(ClampDelay((int)Math.Round(f.Delay * percent / 100.0))));
    }

    /// <summary>Tüm kareleri hedef kare hızına uyacak sabit gecikmeye ayarlar.</summary>
    public static List<EditorFrame> SetFps(IReadOnlyList<EditorFrame> frames, int fps)
    {
        int delay = ClampDelay((int)Math.Round(1000.0 / Math.Max(1, fps)));
        return frames.Select(f => f.WithDelay(delay)).ToList();
    }

    // ─── Kare azaltma ─────────────────────────────────────────────────────────

    /// <summary>
    /// Her <paramref name="keep"/> karede bir <paramref name="remove"/> kare atar.
    /// Dosya boyutunu düşürmenin en doğrudan yolu.
    /// </summary>
    public static List<EditorFrame> Reduce(IReadOnlyList<EditorFrame> frames, int keep, int remove,
        DelayMergeMode delayMode = DelayMergeMode.Distribute)
    {
        keep = Math.Max(1, keep);
        remove = Math.Max(1, remove);

        if (frames.Count <= keep + 1)
            return frames.ToList();

        var toRemove = new List<int>();
        for (int i = keep; i < frames.Count; i += keep + remove)
        {
            for (int r = 0; r < remove && i + r < frames.Count; r++)
                toRemove.Add(i + r);
        }

        // Son kare korunur; animasyonun bitişi ani kesilmesin.
        toRemove.Remove(frames.Count - 1);
        if (toRemove.Count == 0 || toRemove.Count >= frames.Count)
            return frames.ToList();

        return Remove(frames, toRemove, delayMode);
    }

    // ─── Yinelenen kareler ────────────────────────────────────────────────────

    /// <summary>
    /// Ardışık benzer kareleri siler.
    /// </summary>
    /// <param name="similarity">
    /// Silmek için gereken en düşük benzerlik yüzdesi (0-100).
    /// 100 yalnızca birebir aynı kareleri siler.
    /// </param>
    /// <param name="removal">Çiftin hangi karesinin atılacağı.</param>
    /// <param name="delayMode">Silinen karenin gecikmesine ne olacağı.</param>
    /// <param name="keepFramesWithInput">
    /// Girdi etkinliği (tıklama/tuş) taşıyan kareleri koru — vurgular kaybolmasın.
    /// </param>
    public static List<EditorFrame> RemoveDuplicates(IReadOnlyList<EditorFrame> frames,
        double similarity = 100, DuplicateRemoval removal = DuplicateRemoval.Last,
        DelayMergeMode delayMode = DelayMergeMode.AddToPrevious, bool keepFramesWithInput = true)
    {
        if (frames.Count < 2)
            return frames.ToList();

        double threshold = Math.Clamp(similarity, 0, 100);
        var toRemove = new List<int>();
        int lastKept = 0;

        for (int i = 1; i < frames.Count; i++)
        {
            if (keepFramesWithInput && frames[i].Input.HasAnyInput)
            {
                lastKept = i;
                continue;
            }

            // Korunan son kareyle karşılaştır; yoksa yavaşça sürüklenen
            // değişiklikler hiç fark edilmez.
            if (Similarity(frames[lastKept].Pixels, frames[i].Pixels) < threshold)
            {
                lastKept = i;
                continue;
            }

            toRemove.Add(removal == DuplicateRemoval.First ? lastKept : i);

            if (removal == DuplicateRemoval.First)
                lastKept = i;
        }

        return toRemove.Count == 0 ? frames.ToList() : Remove(frames, toRemove, delayMode);
    }

    /// <summary>
    /// Sondan başlayarak ilk kareye yeterince benzeyen kareyi bulur ve
    /// sonrasını atar; döngü dikişsiz kapanır.
    /// </summary>
    /// <returns>Atılan kare sayısı 0 ise özgün liste.</returns>
    public static List<EditorFrame> SmoothLoop(IReadOnlyList<EditorFrame> frames,
        double similarity = 95, int minimumFrames = 2)
    {
        if (frames.Count <= minimumFrames)
            return frames.ToList();

        double threshold = Math.Clamp(similarity, 0, 100);

        for (int i = frames.Count - 1; i >= minimumFrames; i--)
        {
            if (Similarity(frames[0].Pixels, frames[i].Pixels) < threshold)
                continue;

            // i. kare ilk kareyle neredeyse aynı: döngü zaten oraya saracağı için
            // i ve sonrası gereksiz. 0..i-1 aralığını tut.
            return Trim(frames, 0, i - 1);
        }

        return frames.ToList();
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    /// <summary>İki karenin eşleşen piksel yüzdesi (0-100).</summary>
    internal static double Similarity(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        var pa = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(a.AsSpan());
        var pb = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(b.AsSpan());

        int same = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            if (pa[i] == pb[i])
                same++;
        }

        return same * 100.0 / pa.Length;
    }

    private static List<EditorFrame> MapSelected(IReadOnlyList<EditorFrame> frames,
        IEnumerable<int> indexes, Func<EditorFrame, EditorFrame> map)
    {
        var selected = new HashSet<int>(indexes.Where(i => i >= 0 && i < frames.Count));
        if (selected.Count == 0)
            return frames.ToList();

        var result = new List<EditorFrame>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
            result.Add(selected.Contains(i) ? map(frames[i]) : frames[i]);

        return result;
    }

    /// <summary>Biriken gecikmeyi kalan karelere eşit paylaştırır.</summary>
    private static void DistributeDelay(List<EditorFrame> frames, int totalMs)
    {
        if (frames.Count == 0 || totalMs <= 0)
            return;

        int share = totalMs / frames.Count;
        int remainder = totalMs % frames.Count;

        for (int i = 0; i < frames.Count; i++)
        {
            int extra = share + (i < remainder ? 1 : 0);
            if (extra > 0)
                frames[i] = frames[i].WithDelay(ClampDelay(frames[i].Delay + extra));
        }
    }

    private static int ClampDelay(int delayMs) => Math.Clamp(delayMs, MinDelayMs, MaxDelayMs);
}
