using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// Palet üreten kuantalayıcıların ortak temeli.
/// Piksel → indeks eşlemesi <see cref="PaletteMap"/> tarafından yapılır;
/// buradaki iş yalnızca temsili renk kümesini bulmaktır.
/// </summary>
internal abstract class Quantizer
{
    /// <summary>BGRA girdide piksel başına bayt.</summary>
    protected const int BytesPerPixel = 4;

    public int MaxColors { get; set; } = 256;
    public WpfColor? TransparentColor { get; set; }

    /// <summary>Piksel verisini tarayıp iç durumu doldurur.</summary>
    internal abstract void FirstPass(byte[] bgra);

    /// <summary>Taramanın sonucundan paleti üretir.</summary>
    internal abstract List<WpfColor> BuildPalette();

    /// <summary>Tek çağrıda tarama + palet üretimi.</summary>
    public List<WpfColor> Quantize(byte[] bgra)
    {
        FirstPass(bgra);
        return BuildPalette();
    }
}
