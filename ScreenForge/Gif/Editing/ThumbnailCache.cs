using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Kare küçük resimlerini piksel dizisine göre önbelleğe alır.
/// </summary>
/// <remarks>
/// Kare pikselleri değişmezdir: sıralama işlemleri (ters çevirme, taşıma, silme,
/// çoğaltma) aynı dizileri yeniden kullanır. Bu yüzden anahtar olarak dizinin
/// <b>kimliği</b> kullanılabilir; içeriği yeniden okumaya gerek kalmaz.
/// <para>
/// Önbellek olmadan her düzenleme tüm kareleri yeniden ölçekliyordu; 1920×1080
/// ve 60 karede bu, düzenleme başına yarım gigabaytlık bitmap işi demekti ve
/// arayüzü kilitliyordu.
/// </para>
/// <para>
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> kullanıldığı için bir kare
/// artık kullanılmadığında küçük resmi de otomatik toplanır.
/// </para>
/// </remarks>
internal sealed class ThumbnailCache
{
    private readonly ConditionalWeakTable<byte[], ImageSource> _cache = new();
    private readonly int _targetWidth;

    private int _width;
    private int _height;

    public ThumbnailCache(int targetWidth = 76) => _targetWidth = Math.Max(8, targetWidth);

    /// <summary>
    /// Kare boyutu değiştiğinde önbellek geçersizdir; eski küçük resimler
    /// yeni en-boy oranını yansıtmaz.
    /// </summary>
    public void SetFrameSize(int width, int height)
    {
        if (_width == width && _height == height)
            return;

        _width = width;
        _height = height;
        _cache.Clear();
    }

    /// <summary>Verilen karenin küçük resmini döndürür; yoksa üretir.</summary>
    public ImageSource Get(byte[] pixels)
    {
        if (_cache.TryGetValue(pixels, out var cached))
            return cached;

        var thumbnail = Render(pixels);
        _cache.Add(pixels, thumbnail);
        return thumbnail;
    }

    public void Clear() => _cache.Clear();

    private ImageSource Render(byte[] pixels)
    {
        if (_width <= 0 || _height <= 0)
            return CreatePlaceholder();

        var source = BitmapSource.Create(_width, _height, 96, 96,
            PixelFormats.Bgra32, null, pixels, _width * 4);

        double scale = Math.Min(1.0, _targetWidth / (double)_width);
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static ImageSource CreatePlaceholder()
    {
        var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        empty.Freeze();
        return empty;
    }
}
