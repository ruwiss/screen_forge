using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// Palet içinde en yakın rengi bulan hızlı arayıcı.
/// RGB'yi 5-5-5 bite indirger ve 32768 girişlik tembel bir önbellek tutar;
/// böylece piksel başına O(palet) tarama yerine ilk isabetten sonra O(1) olur.
/// </summary>
internal sealed class PaletteMap
{
    private const int CacheBits = 5;
    private const int CacheSize = 1 << (CacheBits * 3); // 32768

    private readonly byte[] _reds;
    private readonly byte[] _greens;
    private readonly byte[] _blues;
    private readonly int _count;
    private readonly byte[] _cache = new byte[CacheSize];
    private readonly bool[] _cached = new bool[CacheSize];

    /// <summary>Saydam palet girdisinin indeksi; yoksa -1.</summary>
    public int TransparentIndex { get; }

    public PaletteMap(IReadOnlyList<WpfColor> palette, int transparentIndex = -1)
    {
        _count = palette.Count;
        _reds = new byte[_count];
        _greens = new byte[_count];
        _blues = new byte[_count];

        for (int i = 0; i < _count; i++)
        {
            _reds[i] = palette[i].R;
            _greens[i] = palette[i].G;
            _blues[i] = palette[i].B;
        }

        TransparentIndex = transparentIndex >= 0 && transparentIndex < _count ? transparentIndex : -1;
    }

    /// <summary>Tam RGB için en yakın palet indeksi (önbellekli, 5-5-5 kuantalı).</summary>
    public byte Map(int r, int g, int b)
    {
        int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
        if (_cached[key])
            return _cache[key];

        byte index = FindNearest(r, g, b);
        _cache[key] = index;
        _cached[key] = true;
        return index;
    }

    /// <summary>Önbelleksiz tam arama — dithering gibi hata yayılımlı akışlar için.</summary>
    public byte FindNearest(int r, int g, int b)
    {
        int best = 0;
        int bestDist = int.MaxValue;

        for (int i = 0; i < _count; i++)
        {
            if (i == TransparentIndex)
                continue;

            int dr = r - _reds[i];
            int dg = g - _greens[i];
            int db = b - _blues[i];
            int dist = dr * dr + dg * dg + db * db;

            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = i;
            if (dist == 0)
                break;
        }

        return (byte)best;
    }

    public byte RedOf(int index) => _reds[index];
    public byte GreenOf(int index) => _greens[index];
    public byte BlueOf(int index) => _blues[index];
}
