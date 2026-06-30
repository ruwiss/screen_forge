using System.Collections;
using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

internal abstract class Quantizer
{
    private readonly bool _singlePass;
    private readonly Hashtable _colorMap = new();

    public int Depth { get; set; } = 4;
    public int MaxColors { get; set; } = 256;
    public int MaxColorsWithTransparency { get; set; }
    public List<WpfColor> ColorTable { get; set; } = new();
    public WpfColor? TransparentColor { get; set; }

    protected Quantizer(bool singlePass) => _singlePass = singlePass;

    public byte[] Quantize(byte[] pixels)
    {
        if (!_singlePass) FirstPass(pixels);
        ColorTable = BuildPalette();
        return SecondPass(pixels);
    }

    internal virtual void FirstPass(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += Depth)
            InitialQuantizePixel(WpfColor.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]));
    }

    internal List<WpfColor> GetPalette() => ColorTable = BuildPalette();

    internal virtual byte[] SecondPass(byte[] pixels)
    {
        var output = new List<byte>();
        for (var index = 0; index < pixels.Length; index += Depth)
        {
            if (pixels[index + 3] == 0) { output.Add((byte)(ColorTable.Count - 1)); continue; }

            var pixel = new WpfColor
            {
                B = pixels[index],
                G = pixels[index + 1],
                R = pixels[index + 2],
                A = pixels[index + 3],
            };

            var hash = BitConverter.ToInt32(new[] { byte.MaxValue, pixel.R, pixel.G, pixel.B }, 0);
            if (_colorMap.ContainsKey(hash)) { output.Add((byte)_colorMap[hash]!); continue; }

            var position = QuantizePixel(pixel);
            output.Add(position);
            _colorMap.Add(hash, position);
        }
        return output.ToArray();
    }

    protected virtual void InitialQuantizePixel(WpfColor pixel) { }
    protected abstract byte QuantizePixel(WpfColor pixel);
    internal abstract List<WpfColor> BuildPalette();
}
