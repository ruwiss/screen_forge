using SkiaSharp;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Yeni nesnelere ayırt edici ad ve renk verir.
/// </summary>
/// <remarks>
/// Şeritte yan yana duran klipleri birbirinden ayırmak için her nesne kendi
/// tonunu alır. Renkler eşit aralıklı ton açılarından seçilir; doygunluk ve
/// parlaklık sabit tutulduğu için hepsi koyu arayüzde okunaklı kalır.
/// </remarks>
public static class ObjectPalette
{
    // Turuncu vurgu renginden başlayıp ton çemberinde eşit adımlarla ilerler.
    private const float StartHue = 28f;
    private const float HueStep = 47f;

    private const float Saturation = 0.72f;
    private const float Brightness = 0.92f;

    /// <summary>Sıradaki nesnenin şerit rengi.</summary>
    public static SKColor ColorFor(int index)
    {
        float hue = (StartHue + index * HueStep) % 360f;
        return SKColor.FromHsv(hue, Saturation * 100f, Brightness * 100f);
    }

    /// <summary>
    /// Aynı türden nesneler için sıralı ad üretir: "Dikdörtgen 1", "Dikdörtgen 2".
    /// </summary>
    public static string NameFor(string baseName, int ordinal) => $"{baseName} {ordinal}";
}
