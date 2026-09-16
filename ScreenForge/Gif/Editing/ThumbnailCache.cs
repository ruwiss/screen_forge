using System.Buffers;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Kare küçük resimlerini kare kimliğine göre önbelleğe alır.
/// </summary>
/// <remarks>
/// Küçük resim, karenin tam boy kopyası tutulmadan üretilir. Aksi hâlde
/// 1080p zaman çizelgesi kare başı bir BitmapSource daha (~8 MB) tutardı.
/// <para>
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> kullanıldığı için bir kare
/// artık kullanılmadığında küçük resmi de otomatik toplanır.
/// </para>
/// </remarks>
internal sealed class ThumbnailCache
{
    private readonly ConditionalWeakTable<EditorFrame, ImageSource> _cache = new();
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
    public ImageSource Get(EditorFrame frame)
    {
        if (_cache.TryGetValue(frame, out var cached))
            return cached;

        var thumbnail = Render(frame);
        _cache.Add(frame, thumbnail);
        return thumbnail;
    }

    public void Clear() => _cache.Clear();

    private ImageSource Render(EditorFrame frame)
    {
        if (_width <= 0 || _height <= 0)
            return CreatePlaceholder();

        double scale = Math.Min(1.0, _targetWidth / (double)_width);
        int dstW = Math.Max(1, (int)Math.Round(_width * scale));
        int dstH = Math.Max(1, (int)Math.Round(_height * scale));

        int raw = frame.RawLength;
        var rented = ArrayPool<byte>.Shared.Rent(raw);
        try
        {
            FrameStore.Decompress(frame.Packed, rented.AsSpan(0, raw));
            var small = Downscale(rented, _width, _height, dstW, dstH);
            var source = BitmapSource.Create(dstW, dstH, 96, 96,
                PixelFormats.Bgra32, null, small, dstW * 4);
            source.Freeze();
            return source;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static byte[] Downscale(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        for (int y = 0; y < dstH; y++)
        {
            int srcY = y * srcH / dstH;
            int srcRow = srcY * srcW;
            int dstRow = y * dstW;
            for (int x = 0; x < dstW; x++)
            {
                int si = (srcRow + x * srcW / dstW) * 4;
                int di = (dstRow + x) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        return dst;
    }

    private static ImageSource CreatePlaceholder()
    {
        var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        empty.Freeze();
        return empty;
    }
}
