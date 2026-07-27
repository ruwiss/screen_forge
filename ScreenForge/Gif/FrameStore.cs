using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace ScreenForge.Gif;

/// <summary>
/// Yakalanan kareleri sıkıştırılmış olarak tutar.
/// </summary>
/// <remarks>
/// Ham BGRA kareler çok yer kaplar: 1920×1080 tek kare 8.3 MB'dır, yani 512 MB
/// yalnızca ~60 kareye yeter. Ekran görüntüleri geniş düz renk alanları
/// içerdiğinden Deflate burada 25-30 kat kazanç sağlar ve kare başına birkaç
/// milisaniye tutar. Bu sayede aynı bellekle onlarca kat uzun kayıt yapılır.
/// <para>
/// <see cref="CompressionLevel.Fastest"/> bilinçli seçimdir: <c>Optimal</c> iki
/// kat daha iyi sıkıştırır ama kare başına ~35 ms sürer ve yakalama hızını düşürür.
/// </para>
/// </remarks>
internal sealed class FrameStore
{
    private readonly List<byte[]> _compressed = new();
    private readonly int _frameByteCount;

    private long _compressedBytes;

    public FrameStore(int frameByteCount) => _frameByteCount = frameByteCount;

    public int Count => _compressed.Count;

    /// <summary>Sıkıştırılmış hâlde kullanılan bellek.</summary>
    public long CompressedBytes => _compressedBytes;

    /// <summary>Sıkıştırılmamış olsaydı kaplayacağı bellek.</summary>
    public long RawBytes => (long)_compressed.Count * _frameByteCount;

    /// <summary>Elde edilen sıkıştırma oranı; kare yoksa 1.</summary>
    public double Ratio => _compressedBytes <= 0 ? 1 : RawBytes / (double)_compressedBytes;

    /// <summary>Kareyi sıkıştırıp saklar ve kapladığı baytı döndürür.</summary>
    public long Add(byte[] pixels)
    {
        var packed = Compress(pixels);
        _compressed.Add(packed);
        _compressedBytes += packed.Length;
        return packed.Length;
    }

    /// <summary>Belirtilen kareyi açar.</summary>
    public byte[] Get(int index) => Decompress(_compressed[index], _frameByteCount);

    /// <summary>
    /// Son eklenen kareyi geri alır.
    /// Bellek sınırı ancak sıkıştırma sonrası bilindiği için gerekir.
    /// </summary>
    public void RemoveLast(long storedBytes)
    {
        if (_compressed.Count == 0)
            return;

        _compressed.RemoveAt(_compressed.Count - 1);
        _compressedBytes -= storedBytes;
    }

    /// <summary>Tüm kareleri açar ve depoyu boşaltır.</summary>
    public List<byte[]> DrainAll()
    {
        var frames = new List<byte[]>(_compressed.Count);
        foreach (var packed in _compressed)
            frames.Add(Decompress(packed, _frameByteCount));

        Clear();
        return frames;
    }

    public void Clear()
    {
        _compressed.Clear();
        _compressedBytes = 0;
    }

    // ─── Sıkıştırma ───────────────────────────────────────────────────────────

    internal static byte[] Compress(byte[] pixels)
    {
        // Sıkıştırılmış çıktı nadiren girdiden büyük olur; tampon buna göre ayrılır.
        using var output = new MemoryStream(Math.Max(1024, pixels.Length / 8));

        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(pixels, 0, pixels.Length);

        return output.ToArray();
    }

    internal static byte[] Decompress(byte[] packed, int expectedLength)
    {
        var result = new byte[expectedLength];

        using var input = new MemoryStream(packed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);

        int offset = 0;
        while (offset < expectedLength)
        {
            int read = deflate.Read(result, offset, expectedLength - offset);
            if (read <= 0)
                break;

            offset += read;
        }

        return result;
    }
}
