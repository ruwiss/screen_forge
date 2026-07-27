using System.IO;
using System.Windows;
using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// Animasyonlu GIF yazıcı. ScreenToGif (Nicke Manarin) mimarisinden uyarlandı.
/// Kullanım: <see cref="SetCanvasSize"/> → (opsiyonel) <see cref="BuildGlobalPalette"/> →
/// her kare için <see cref="AddFrame"/> → <see cref="Dispose"/> ile trailer yaz.
/// </summary>
internal sealed class GifFile : IDisposable
{
    /// <summary>0 = sonsuz döngü, -1 = döngü yok.</summary>
    public int RepeatCount { get; set; }

    /// <summary>Kare başına maksimum renk (2-256).</summary>
    public int MaximumNumberColor { get; set; } = 256;

    /// <summary>Neural = kaliteli (varsayılan), Octree = hızlı.</summary>
    public QuantizerType QuantizerType { get; set; } = QuantizerType.Neural;

    /// <summary>Neural örnekleme faktörü: 1 = en iyi kalite, 20 = en hızlı.</summary>
    public int SamplingFactor { get; set; } = 5;

    /// <summary>Tüm kareler tek bir global palet kullanır; palet tekrarı olmaz, dosya küçülür.</summary>
    public bool UseGlobalPalette { get; set; }

    /// <summary>Floyd-Steinberg dithering — gradyan/fotoğraf kalitesini artırır.</summary>
    public bool UseDithering { get; set; }

    private const int DisposalLeave = 1; // önceki kareyi olduğu yerde bırak (delta kareler için şart)

    private readonly Stream _stream;
    private bool _headerWritten;
    private bool _disposed;
    private int _canvasWidth;
    private int _canvasHeight;

    private List<WpfColor>? _globalPalette;
    private PaletteMap? _globalMap;
    private int _globalSizeField;

    public GifFile(Stream stream) => _stream = stream;

    /// <summary>Mantıksal ekran boyutu. İlk <see cref="AddFrame"/> çağrısından önce verilmeli.</summary>
    public void SetCanvasSize(int width, int height)
    {
        _canvasWidth = width;
        _canvasHeight = height;
    }

    /// <summary>
    /// Verilen örnek karelerden tek bir global palet üretir.
    /// Yalnızca <see cref="UseGlobalPalette"/> açıkken anlamlıdır.
    /// </summary>
    public void BuildGlobalPalette(IReadOnlyList<byte[]> samples)
    {
        if (!UseGlobalPalette || samples.Count == 0)
            return;

        var merged = MergeSamples(samples);
        // Global palette delta karelerde saydamlık gerektirir → bir slot ayır.
        int maxColors = Math.Clamp(MaximumNumberColor, 2, 256) - 1;
        _globalPalette = BuildPalette(merged, maxColors);
        _globalPalette.Add(WpfColor.FromRgb(0, 0, 0)); // saydam slot (son indeks)
        _globalMap = new PaletteMap(_globalPalette, _globalPalette.Count - 1);
        _globalSizeField = SizeField(_globalPalette.Count);
    }

    /// <summary>
    /// Kareyi yazar. <paramref name="bgra"/>, <paramref name="rect"/> boyutunda BGRA verisidir.
    /// Alfa değeri 0 olan pikseller "değişmedi" kabul edilip saydam yazılır.
    /// </summary>
    public void AddFrame(byte[] bgra, Int32Rect rect, int delayMs, bool hasTransparency)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        List<WpfColor> palette;
        PaletteMap map;
        int transparentIndex;

        if (UseGlobalPalette && _globalMap != null && _globalPalette != null)
        {
            palette = _globalPalette;
            map = _globalMap;
            transparentIndex = _globalMap.TransparentIndex;
        }
        else
        {
            int maxColors = Math.Clamp(MaximumNumberColor, 2, 256);
            if (hasTransparency)
                maxColors--;

            palette = BuildPalette(bgra, maxColors);
            if (hasTransparency)
            {
                palette.Add(WpfColor.FromRgb(0, 0, 0));
                transparentIndex = palette.Count - 1;
            }
            else
            {
                transparentIndex = -1;
            }

            map = new PaletteMap(palette, transparentIndex);
        }

        var indexed = UseDithering
            ? MapDithered(bgra, rect.Width, rect.Height, map, transparentIndex)
            : MapDirect(bgra, map, transparentIndex);

        int sizeField = UseGlobalPalette && _globalPalette != null ? _globalSizeField : SizeField(palette.Count);

        if (!_headerWritten)
        {
            WriteHeader(sizeField);
            _headerWritten = true;
        }

        WriteGraphicControlExtension(delayMs, transparentIndex);
        WriteImageDescriptor(rect, sizeField);
        if (!UseGlobalPalette)
            WritePalette(palette, sizeField);

        new LzwEncoder(indexed, sizeField + 1).Encode(_stream);
    }

    // ─── Palet üretimi ────────────────────────────────────────────────────────

    private List<WpfColor> BuildPalette(byte[] bgra, int maxColors)
    {
        maxColors = Math.Clamp(maxColors, 2, 256);

        Quantizer quantizer = QuantizerType == QuantizerType.Octree
            ? new OctreeQuantizer { MaxColors = maxColors }
            : new NeuralQuantizer(Math.Clamp(SamplingFactor, 1, 20), maxColors) { MaxColors = maxColors };

        quantizer.TransparentColor = null;
        quantizer.FirstPass(bgra);
        var palette = quantizer.BuildPalette();

        if (palette.Count == 0)
            palette.Add(WpfColor.FromRgb(0, 0, 0));
        if (palette.Count > maxColors)
            palette.RemoveRange(maxColors, palette.Count - maxColors);

        return palette;
    }

    /// <summary>Global palet için kareleri seyreltip tek bir örnek tampona toplar.</summary>
    private static byte[] MergeSamples(IReadOnlyList<byte[]> samples)
    {
        const int MaxSampleBytes = 8 * 1024 * 1024; // 2M piksel — palet için fazlasıyla yeterli

        long total = 0;
        foreach (var s in samples) total += s.LongLength;
        if (total <= MaxSampleBytes)
        {
            var all = new byte[total];
            int offset = 0;
            foreach (var s in samples)
            {
                Buffer.BlockCopy(s, 0, all, offset, s.Length);
                offset += s.Length;
            }
            return all;
        }

        // Piksel adımlayarak eşit dağılımlı örnek al.
        int stride = (int)Math.Ceiling((double)total / MaxSampleBytes);
        var buffer = new byte[MaxSampleBytes];
        int written = 0;

        foreach (var s in samples)
        {
            for (int i = 0; i + 3 < s.Length && written + 3 < buffer.Length; i += 4 * stride)
            {
                buffer[written++] = s[i];
                buffer[written++] = s[i + 1];
                buffer[written++] = s[i + 2];
                buffer[written++] = s[i + 3];
            }
        }

        if (written == buffer.Length)
            return buffer;

        var trimmed = new byte[written];
        Buffer.BlockCopy(buffer, 0, trimmed, 0, written);
        return trimmed;
    }

    // ─── Piksel → indeks eşleme ───────────────────────────────────────────────

    private static byte[] MapDirect(byte[] bgra, PaletteMap map, int transparentIndex)
    {
        var output = new byte[bgra.Length / 4];
        byte transparent = (byte)Math.Max(0, transparentIndex);

        for (int i = 0, p = 0; p < output.Length; i += 4, p++)
        {
            if (transparentIndex >= 0 && bgra[i + 3] == 0)
            {
                output[p] = transparent;
                continue;
            }

            output[p] = map.Map(bgra[i + 2], bgra[i + 1], bgra[i]);
        }

        return output;
    }

    /// <summary>
    /// Floyd-Steinberg dithering ile tek geçişte eşleme.
    /// Hata yalnızca iki satırlık kayan tamponda tutulur → bellek O(genişlik).
    /// </summary>
    private static byte[] MapDithered(byte[] bgra, int width, int height, PaletteMap map, int transparentIndex)
    {
        int pixelCount = bgra.Length / 4;
        if (width <= 0 || height <= 0 || width * height != pixelCount)
            return MapDirect(bgra, map, transparentIndex);

        var output = new byte[pixelCount];
        byte transparent = (byte)Math.Max(0, transparentIndex);

        // [0] = mevcut satır, [1] = sonraki satır; her piksel için R,G,B hatası
        var curr = new float[(width + 2) * 3];
        var next = new float[(width + 2) * 3];

        for (int y = 0; y < height; y++)
        {
            Array.Clear(next);

            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int i = p * 4;

                if (transparentIndex >= 0 && bgra[i + 3] == 0)
                {
                    output[p] = transparent;
                    continue;
                }

                int e = (x + 1) * 3;
                float r = bgra[i + 2] + curr[e];
                float g = bgra[i + 1] + curr[e + 1];
                float b = bgra[i] + curr[e + 2];

                int ri = Clamp255(r), gi = Clamp255(g), bi = Clamp255(b);
                byte index = map.FindNearest(ri, gi, bi);
                output[p] = index;

                float er = r - map.RedOf(index);
                float eg = g - map.GreenOf(index);
                float eb = b - map.BlueOf(index);

                Spread(curr, e + 3, er, eg, eb, 7f / 16f);
                Spread(next, e - 3, er, eg, eb, 3f / 16f);
                Spread(next, e, er, eg, eb, 5f / 16f);
                Spread(next, e + 3, er, eg, eb, 1f / 16f);
            }

            (curr, next) = (next, curr);
        }

        return output;
    }

    private static void Spread(float[] buffer, int offset, float r, float g, float b, float factor)
    {
        buffer[offset] += r * factor;
        buffer[offset + 1] += g * factor;
        buffer[offset + 2] += b * factor;
    }

    private static int Clamp255(float value) => value <= 0 ? 0 : value >= 255 ? 255 : (int)value;

    // ─── Blok yazıcılar ───────────────────────────────────────────────────────

    private void WriteHeader(int sizeField)
    {
        WriteString("GIF89a");
        WriteShort(_canvasWidth > 0 ? _canvasWidth : 1);
        WriteShort(_canvasHeight > 0 ? _canvasHeight : 1);

        bool globalTable = UseGlobalPalette && _globalPalette != null;
        int packed = 0x70 | (globalTable ? 0x80 | (sizeField & 0x07) : 0); // renk çözünürlüğü = 8 bit
        WriteByte(packed);
        WriteByte(0); // arka plan rengi indeksi
        WriteByte(0); // piksel en-boy oranı

        if (globalTable)
            WritePalette(_globalPalette!, sizeField);

        if (RepeatCount > -1)
            WriteApplicationExtension();
    }

    private void WritePalette(List<WpfColor> palette, int sizeField)
    {
        foreach (var color in palette)
        {
            WriteByte(color.R);
            WriteByte(color.G);
            WriteByte(color.B);
        }

        int slots = 2 << sizeField;
        for (int i = palette.Count; i < slots; i++)
        {
            WriteByte(0);
            WriteByte(0);
            WriteByte(0);
        }
    }

    private void WriteApplicationExtension()
    {
        WriteByte(0x21);
        WriteByte(0xff);
        WriteByte(0x0b);
        WriteString("NETSCAPE2.0");
        WriteByte(0x03);
        WriteByte(0x01);
        WriteShort(RepeatCount);
        WriteByte(0x00);
    }

    private void WriteGraphicControlExtension(int delayMs, int transparentIndex)
    {
        WriteByte(0x21);
        WriteByte(0xf9);
        WriteByte(0x04);

        // bit 4-2 disposal, bit 1 kullanıcı girdisi, bit 0 saydamlık
        int packed = (DisposalLeave & 0x07) << 2;
        if (transparentIndex >= 0)
            packed |= 0x01;
        WriteByte(packed);

        // GIF gecikmesi 1/100 sn birimindedir; 0 tarayıcılarda "olabildiğince hızlı"ya düşer.
        int centiseconds = (int)Math.Round(delayMs / 10.0, MidpointRounding.AwayFromZero);
        WriteShort(Math.Clamp(centiseconds, 1, ushort.MaxValue));

        WriteByte(transparentIndex >= 0 ? transparentIndex : 0);
        WriteByte(0);
    }

    private void WriteImageDescriptor(Int32Rect rect, int sizeField)
    {
        WriteByte(0x2c);
        WriteShort(rect.X);
        WriteShort(rect.Y);
        WriteShort(rect.Width);
        WriteShort(rect.Height);

        // bit 7 yerel palet, bit 6 interlace, bit 5 sıralı, bit 2-0 palet boyutu
        WriteByte(UseGlobalPalette ? 0 : 0x80 | (sizeField & 0x07));
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    /// <summary>Palet boyutu alanı: girdi sayısı 2^(n+1) olacak şekilde en küçük n (0-7).</summary>
    internal static int SizeField(int count)
    {
        int n = 0;
        while (n < 7 && (2 << n) < count) n++;
        return n;
    }

    private void WriteByte(int value) => _stream.WriteByte((byte)value);

    private void WriteShort(int value)
    {
        _stream.WriteByte((byte)(value & 0xff));
        _stream.WriteByte((byte)((value >> 8) & 0xff));
    }

    private void WriteString(string value)
    {
        foreach (char c in value)
            _stream.WriteByte((byte)c);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        WriteByte(0x3b); // GIF trailer
        _stream.Flush();
    }
}
