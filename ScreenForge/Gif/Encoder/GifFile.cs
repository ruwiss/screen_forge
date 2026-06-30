using System.Collections;
using System.IO;
using System.Windows;
using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// Animated GIF writer. Adapted from ScreenToGif (Nicke Manarin).
/// Caller: AddFrame() per frame, then Dispose() to finalize the stream.
/// </summary>
internal sealed class GifFile : IDisposable
{
    public int RepeatCount { get; set; } = 0;
    public bool UseFullTransparency { get; set; }
    public WpfColor? TransparentColor { get; set; }
    public int MaximumNumberColor { get; set; } = 256;

    /// <summary>Neural=kaliteli (varsayılan), Octree=hızlı</summary>
    public QuantizerType QuantizerType { get; set; } = QuantizerType.Neural;

    /// <summary>Neural quantizer örnekleme faktörü: 1=en iyi kalite, 20=en hızlı. Varsayılan: 5.</summary>
    public int SamplingFactor { get; set; } = 5;

    /// <summary>İlk frame'den palette oluştur, sonraki frameler aynı global palette kullanır. Dosya boyutu -40%.</summary>
    public bool UseGlobalPalette { get; set; } = false;

    /// <summary>Floyd-Steinberg dithering — gradient/fotoğraf kalitesini artırır.</summary>
    public bool UseDithering { get; set; } = false;

    private readonly Stream _stream;
    private bool _isFirstFrame = true;
    private byte[] _indexedPixels = Array.Empty<byte>();
    private List<WpfColor> _colorTable = new();
    private List<WpfColor> _globalPalette = new();
    private bool _colorTableHasTransparency;
    private int _colorTableSize;

    public GifFile(Stream stream) => _stream = stream;

    private int _frameWidth, _frameHeight;

    public void AddFrame(byte[] pixels, Int32Rect rect, int delayMs = 100, bool isLastFrame = false)
    {
        _frameWidth  = rect.Width;
        _frameHeight = rect.Height;
        ReadPixels(pixels);

        CalculateColorTableSize();

        if (_isFirstFrame)
        {
            WriteLogicalScreenDescriptor(rect);
            if (RepeatCount > -1) WriteApplicationExtension();
        }

        WriteGraphicControlExtension(delayMs, isLastFrame);
        WriteImageDescriptor(rect);
        WritePalette();
        WriteImage();

        _isFirstFrame = false;
    }

    // ─── Private write methods ────────────────────────────────────────────────

    private void WriteLogicalScreenDescriptor(Int32Rect rect)
    {
        WriteString("GIF89a");
        WriteShort(rect.Width);
        WriteShort(rect.Height);

        var bitArray = new BitArray(8);
        bitArray.Set(0, false); // no global color table
        var pixelBits = ToBitValues(ColorTableSize());
        bitArray.Set(1, pixelBits[0]); bitArray.Set(2, pixelBits[1]); bitArray.Set(3, pixelBits[2]);
        bitArray.Set(4, true);
        bitArray.Set(5, false); bitArray.Set(6, false); bitArray.Set(7, false); // no global ct size

        WriteByte(ConvertToByte(bitArray));
        WriteByte(UseFullTransparency ? FindTransparentColorIndex() : 0);
        WriteByte(0);
    }

    private void WritePalette()
    {
        foreach (var color in _colorTable) { WriteByte(color.R); WriteByte(color.G); WriteByte(color.B); }
        var empty = (GetMaximumColorCount() - _colorTable.Count) * 3;
        for (var i = 0; i < empty; i++) WriteByte(0);
    }

    private void WriteApplicationExtension()
    {
        WriteByte(0x21); WriteByte(0xff); WriteByte(0x0b);
        WriteString("NETSCAPE2.0");
        WriteByte(0x03); WriteByte(0x01); WriteShort(RepeatCount); WriteByte(0x00);
    }

    private void WriteGraphicControlExtension(int delayMs, bool isLastFrame)
    {
        WriteByte(0x21); WriteByte(0xf9); WriteByte(0x04);

        var b = new BitArray(8);
        b.Set(0, false); b.Set(1, false); b.Set(2, false);

        if (UseFullTransparency)
        {
            if (isLastFrame) { b.Set(3, false); b.Set(4, false); b.Set(5, true); }
            else             { b.Set(3, false); b.Set(4, true);  b.Set(5, false); }
        }
        else
        {
            if (_isFirstFrame) { b.Set(3, false); b.Set(4, false); b.Set(5, true); }
            else               { b.Set(3, false); b.Set(4, false); b.Set(5, false); }
        }

        b.Set(6, false);
        b.Set(7, (!_isFirstFrame || UseFullTransparency) && _colorTableHasTransparency);
        WriteByte(ConvertToByte(b));

        // GIF delay is in 1/100s units
        WriteShort((int)Math.Round(delayMs / 10.0, MidpointRounding.AwayFromZero));
        WriteByte(FindTransparentColorIndex());
        WriteByte(0);
    }

    private void WriteImageDescriptor(Int32Rect rect)
    {
        WriteByte(0x2c);
        WriteShort(rect.X); WriteShort(rect.Y);
        WriteShort(rect.Width); WriteShort(rect.Height);

        // Always local color table
        var b = new BitArray(8);
        b.Set(0, true);  // local color table flag
        b.Set(1, false); // no interlace
        b.Set(2, true);  // sort flag
        b.Set(3, false); b.Set(4, false);
        var sz = ToBitValues(_colorTableSize);
        b.Set(5, sz[0]); b.Set(6, sz[1]); b.Set(7, sz[2]);
        WriteByte(ConvertToByte(b));
    }

    private void WriteImage()
    {
        var encoder = new LzwEncoder(0, 0, _indexedPixels, 8);
        encoder.Encode(_stream);
    }

    // ─── Quantization ─────────────────────────────────────────────────────────

    private void ReadPixels(byte[] pixels)
    {
        if (UseGlobalPalette && !_isFirstFrame && _globalPalette.Count > 0)
        {
            // Sonraki frameler: global palette üzerinden direkt map
            _colorTable  = _globalPalette;
            _indexedPixels = MapWithPalette(pixels, _globalPalette);
        }
        else
        {
            Quantizer q = QuantizerType == QuantizerType.Octree
                ? new OctreeQuantizer { MaxColors = MaximumNumberColor }
                : new NeuralQuantizer(SamplingFactor, MaximumNumberColor);

            q.MaxColors        = MaximumNumberColor;
            q.TransparentColor = (!_isFirstFrame || UseFullTransparency) ? TransparentColor : null;

            _indexedPixels = q.Quantize(pixels);
            _colorTable    = q.ColorTable;

            if (UseGlobalPalette && _isFirstFrame)
                _globalPalette = _colorTable;
        }

        if (UseDithering && _indexedPixels.Length > 0)
            _indexedPixels = ApplyFloydSteinberg(pixels, _colorTable, _indexedPixels);

        _colorTableHasTransparency = TransparentColor.HasValue && _colorTable.Contains(TransparentColor.Value);
    }

    /// <summary>Var olan palette kullanarak piksel indekslerini bul (global palette modu).</summary>
    private static byte[] MapWithPalette(byte[] pixels, List<WpfColor> palette)
    {
        var output = new byte[pixels.Length / 4];
        for (int i = 0, p = 0; i < pixels.Length; i += 4, p++)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            int best = 0, bestDist = int.MaxValue;
            for (int c = 0; c < palette.Count; c++)
            {
                int dr = r - palette[c].R, dg = g - palette[c].G, db = b - palette[c].B;
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            output[p] = (byte)best;
        }
        return output;
    }

    /// <summary>Floyd-Steinberg dithering — hata yayılımı ile banding azaltır.</summary>
    private byte[] ApplyFloydSteinberg(byte[] pixels, List<WpfColor> palette, byte[] indexed)
    {
        // pixels: BGRA flat, indexed: output palette indices
        // Satır genişliğini indexed uzunluğundan çıkar (kare değil olabilir)
        int pixCount = pixels.Length / 4;
        if (pixCount != indexed.Length) return indexed; // güvenlik

        // Hata buffer'ları (float): her piksel için R,G,B hatası
        var errR = new float[pixCount];
        var errG = new float[pixCount];
        var errB = new float[pixCount];

        int w = _frameWidth;
        int h = _frameHeight;
        if (w <= 0 || h <= 0 || w * h != pixCount) return indexed;

        var result = new byte[indexed.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int pi   = y * w + x;
                int bi   = pi * 4;

                float oldB = pixels[bi]     + errB[pi];
                float oldG = pixels[bi + 1] + errG[pi];
                float oldR = pixels[bi + 2] + errR[pi];

                // En yakın palette rengi bul
                int best = 0, bestDist = int.MaxValue;
                for (int c = 0; c < palette.Count; c++)
                {
                    float dr = oldR - palette[c].R;
                    float dg = oldG - palette[c].G;
                    float db = oldB - palette[c].B;
                    int dist = (int)(dr * dr + dg * dg + db * db);
                    if (dist < bestDist) { bestDist = dist; best = c; }
                }
                result[pi] = (byte)best;

                // Hata dağıt (Floyd-Steinberg katsayıları: 7/16, 3/16, 5/16, 1/16)
                float eR = oldR - palette[best].R;
                float eG = oldG - palette[best].G;
                float eB = oldB - palette[best].B;

                if (x + 1 < w)               { errR[pi + 1]     += eR * 7 / 16f; errG[pi + 1]     += eG * 7 / 16f; errB[pi + 1]     += eB * 7 / 16f; }
                if (y + 1 < h && x > 0)       { errR[pi + w - 1] += eR * 3 / 16f; errG[pi + w - 1] += eG * 3 / 16f; errB[pi + w - 1] += eB * 3 / 16f; }
                if (y + 1 < h)                { errR[pi + w]     += eR * 5 / 16f; errG[pi + w]     += eG * 5 / 16f; errB[pi + w]     += eB * 5 / 16f; }
                if (y + 1 < h && x + 1 < w)   { errR[pi + w + 1] += eR * 1 / 16f; errG[pi + w + 1] += eG * 1 / 16f; errB[pi + w + 1] += eB * 1 / 16f; }
            }
        }
        return result;
    }

    private void CalculateColorTableSize()
    {
        _colorTableSize = _colorTable.Count > 1 ? (int)Math.Log(_colorTable.Count - 1, 2) : 0;
    }

    private int ColorTableSize() => _colorTable.Count > 1 ? (int)Math.Log(_colorTable.Count - 1, 2) : 0;

    private int GetMaximumColorCount() => (int)Math.Pow(2, _colorTableSize + 1);

    private int FindTransparentColorIndex()
    {
        if ((_isFirstFrame && !UseFullTransparency) || !_colorTableHasTransparency) return 0;
        var index = _colorTable.IndexOf(TransparentColor!.Value);
        return index > -1 ? index : 0;
    }

    // ─── Stream helpers ───────────────────────────────────────────────────────

    private void WriteByte(int value) => _stream.WriteByte(Convert.ToByte(value));
    private void WriteShort(int value) { _stream.WriteByte(Convert.ToByte(value & 0xff)); _stream.WriteByte(Convert.ToByte((value >> 8) & 0xff)); }
    private void WriteString(string value) => _stream.Write(value.Select(c => (byte)c).ToArray(), 0, value.Length);

    private static byte ConvertToByte(BitArray bits)
    {
        var bytes = new byte[1];
        new BitArray(bits.Cast<bool>().Reverse().ToArray()).CopyTo(bytes, 0);
        return bytes[0];
    }

    private static bool[] ToBitValues(int number) =>
        new BitArray(new[] { number }).Cast<bool>().Take(3).Reverse().ToArray();

    public void Dispose()
    {
        WriteByte(0x3b); // GIF trailer
        _stream.Flush();
        _stream.Position = 0;
    }
}
