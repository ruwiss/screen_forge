using System.Runtime.InteropServices;
using ScreenForge.Editor;
using SkiaSharp;

namespace ScreenForge.Gif.Editing;

/// <summary>
/// Çizim nesnelerini kare pikselleri üzerine birleştirir.
/// </summary>
/// <remarks>
/// Çizimler karelere kalıcı yazılmaz; yalnızca önizleme ve dışa aktarım
/// sırasında uygulanır. Her nesne kendi görünürlük aralığına ve konum
/// animasyonuna göre çizilir.
/// </remarks>
public static class AnnotationCompositor
{
    /// <summary>
    /// Verilen karede görünen nesneleri çizip sonucu BGRA olarak döndürür.
    /// Çizilecek nesne yoksa özgün dizi döner.
    /// </summary>
    /// <param name="bgra">Kare pikselleri (BGRA, opak).</param>
    /// <param name="width">Çıktı genişliği.</param>
    /// <param name="height">Çıktı yüksekliği.</param>
    /// <param name="track">Çizim katmanı.</param>
    /// <param name="frameIndex">İşlenen karenin sırası.</param>
    /// <param name="sourceWidth">Çizim koordinatlarının ait olduğu genişlik.</param>
    /// <param name="sourceHeight">Çizim koordinatlarının ait olduğu yükseklik.</param>
    /// <param name="skip">Çizilmeyecek nesneler (canlı düzenlenen seçim gibi).</param>
    public static byte[] Apply(byte[] bgra, int width, int height,
        AnnotationTrack track, int frameIndex,
        int sourceWidth = 0, int sourceHeight = 0, IReadOnlyCollection<SceneItem>? skip = null)
    {
        if (width <= 0 || height <= 0)
            return bgra;

        var items = track.ItemsAt(frameIndex);

        if (skip is { Count: > 0 })
            items.RemoveAll(skip.Contains);

        if (items.Count == 0)
            return bgra;

        if (sourceWidth <= 0) sourceWidth = width;
        if (sourceHeight <= 0) sourceHeight = height;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = new SKBitmap(info);
        using var canvas = new SKCanvas(surface);

        // Kareyi zemin olarak yerleştir: nesneler üstüne çizilir.
        DrawFrame(canvas, bgra, width, height);

        // Çizim koordinatları kaynak boyutundadır; çıktı ölçeklendiyse uyarla.
        if (sourceWidth != width || sourceHeight != height)
            canvas.Scale(width / (float)sourceWidth, height / (float)sourceHeight);

        foreach (var item in items)
            DrawAt(canvas, item, track.ClipOf(item).OffsetAt(frameIndex));

        canvas.Flush();
        return ReadOpaque(surface, bgra.Length);
    }

    /// <summary>
    /// Nesneyi o karedeki konumunda çizer.
    /// </summary>
    /// <remarks>
    /// Sapma nesnenin kendisine yazılmaz; tuval ötelenir. Böylece temel
    /// geometri bozulmadan kalır ve her kare bağımsız hesaplanabilir.
    /// </remarks>
    private static void DrawAt(SKCanvas canvas, SceneItem item, SKPoint offset)
    {
        if (offset.X == 0 && offset.Y == 0)
        {
            item.Render(canvas);
            return;
        }

        canvas.Save();
        canvas.Translate(offset.X, offset.Y);
        item.Render(canvas);
        canvas.Restore();
    }

    /// <summary>Herhangi bir karede çizilecek nesne var mı.</summary>
    public static bool HasWork(AnnotationTrack track) => track.HasVisibleItems();

    // ─── Piksel köprüleri ─────────────────────────────────────────────────────

    private static void DrawFrame(SKCanvas canvas, byte[] bgra, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);

        try
        {
            // Kopyasız sarmalama: kare verisi zaten doğru düzende.
            using var frame = new SKBitmap();
            frame.InstallPixels(info, handle.AddrOfPinnedObject(), width * 4);
            canvas.DrawBitmap(frame, 0, 0);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Yüzeyi BGRA dizisine kopyalar ve alfayı opak yapar.
    /// GIF hattı saydamlığı kendi delta mantığı için kullandığından
    /// buradan gelen her piksel opak olmalıdır.
    /// </summary>
    private static byte[] ReadOpaque(SKBitmap surface, int expectedLength)
    {
        var span = surface.GetPixelSpan();
        var output = new byte[expectedLength];

        int length = Math.Min(expectedLength, span.Length);
        span[..length].CopyTo(output);

        var pixels = MemoryMarshal.Cast<byte, uint>(output.AsSpan());
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] |= 0xFF000000u;

        return output;
    }
}
