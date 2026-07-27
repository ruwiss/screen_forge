using System.Runtime.InteropServices;
using System.Windows;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRect = System.Drawing.Rectangle;

namespace ScreenForge.Gif.Editing;

/// <summary>Karelerin döndürülme yönü.</summary>
public enum RotateDirection
{
    Left90,
    Right90,
    Half180,
}

/// <summary>
/// Kare piksellerini dönüştüren işlemler.
/// Hepsi yeni dizi üretir; girdi değiştirilmez.
/// </summary>
public static class ImageOperations
{
    /// <summary>Tüm kareleri verilen dikdörtgene kırpar.</summary>
    public static List<EditorFrame> Crop(IReadOnlyList<EditorFrame> frames, int width, int height, Int32Rect rect)
    {
        rect = ClampRect(rect, width, height);
        if (rect.Width <= 0 || rect.Height <= 0)
            return frames.ToList();

        return frames.Select(f => f.WithPixels(CropPixels(f.Pixels, width, rect))).ToList();
    }

    /// <summary>Tüm kareleri döndürür. 90° dönüşlerde en ve boy yer değiştirir.</summary>
    public static List<EditorFrame> Rotate(IReadOnlyList<EditorFrame> frames, int width, int height, RotateDirection direction)
        => frames.Select(f => f.WithPixels(RotatePixels(f.Pixels, width, height, direction))).ToList();

    /// <summary>Tüm kareleri yeniden boyutlandırır.</summary>
    public static List<EditorFrame> Resize(IReadOnlyList<EditorFrame> frames,
        int width, int height, int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0 || (newWidth == width && newHeight == height))
            return frames.ToList();

        return frames.Select(f => f.WithPixels(ResizePixels(f.Pixels, width, height, newWidth, newHeight))).ToList();
    }

    /// <summary>Döndürme sonrası ortaya çıkacak boyut.</summary>
    public static (int Width, int Height) SizeAfterRotate(int width, int height, RotateDirection direction)
        => direction == RotateDirection.Half180 ? (width, height) : (height, width);

    /// <summary>
    /// Ekranda çizilen seçim dikdörtgenini kaynak piksel koordinatına çevirir.
    /// </summary>
    /// <param name="left">Seçimin tuvaldeki sol kenarı.</param>
    /// <param name="top">Seçimin tuvaldeki üst kenarı.</param>
    /// <param name="width">Seçimin ekrandaki genişliği.</param>
    /// <param name="height">Seçimin ekrandaki yüksekliği.</param>
    /// <param name="zoom">Tuvalin yakınlaştırma çarpanı.</param>
    /// <param name="sourceWidth">Karenin gerçek genişliği.</param>
    /// <param name="sourceHeight">Karenin gerçek yüksekliği.</param>
    /// <param name="minimumSize">Kabul edilecek en küçük kenar uzunluğu.</param>
    /// <returns>Kaynak koordinatındaki dikdörtgen; seçim çok küçükse <see langword="null"/>.</returns>
    public static Int32Rect? ScreenRectToSource(double left, double top, double width, double height,
        double zoom, int sourceWidth, int sourceHeight, int minimumSize = 2)
    {
        if (zoom <= 0 || width <= 0 || height <= 0)
            return null;

        var rect = ClampRect(new Int32Rect(
            (int)Math.Round(left / zoom),
            (int)Math.Round(top / zoom),
            (int)Math.Round(width / zoom),
            (int)Math.Round(height / zoom)), sourceWidth, sourceHeight);

        return rect.Width < minimumSize || rect.Height < minimumSize ? null : rect;
    }

    // ─── Piksel işleyicileri ──────────────────────────────────────────────────

    internal static Int32Rect ClampRect(Int32Rect rect, int width, int height)
    {
        int x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        int y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        int w = Math.Clamp(rect.Width, 0, width - x);
        int h = Math.Clamp(rect.Height, 0, height - y);
        return new Int32Rect(x, y, w, h);
    }

    internal static byte[] CropPixels(byte[] source, int sourceWidth, Int32Rect rect)
    {
        var output = new byte[rect.Width * rect.Height * 4];
        int rowBytes = rect.Width * 4;

        for (int y = 0; y < rect.Height; y++)
        {
            int sourceOffset = ((rect.Y + y) * sourceWidth + rect.X) * 4;
            Buffer.BlockCopy(source, sourceOffset, output, y * rowBytes, rowBytes);
        }

        return output;
    }

    internal static byte[] RotatePixels(byte[] source, int width, int height, RotateDirection direction)
    {
        var src = MemoryMarshal.Cast<byte, int>(source.AsSpan());
        var output = new byte[source.Length];
        var dst = MemoryMarshal.Cast<byte, int>(output.AsSpan());

        switch (direction)
        {
            case RotateDirection.Right90:
                // (x,y) → (height-1-y, x); yeni genişlik = eski yükseklik
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        dst[x * height + (height - 1 - y)] = src[y * width + x];
                break;

            case RotateDirection.Left90:
                // (x,y) → (y, width-1-x)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        dst[(width - 1 - x) * height + y] = src[y * width + x];
                break;

            default: // 180 — boyut değişmez
                int total = width * height;
                for (int i = 0; i < total; i++)
                    dst[total - 1 - i] = src[i];
                break;
        }

        return output;
    }

    internal static byte[] ResizePixels(byte[] source, int width, int height, int newWidth, int newHeight)
    {
        using var sourceBitmap = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        var sourceData = sourceBitmap.LockBits(new DrawingRect(0, 0, width, height),
            DrawingImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(source, 0, sourceData.Scan0, source.Length);
        sourceBitmap.UnlockBits(sourceData);

        using var target = new DrawingBitmap(newWidth, newHeight, DrawingPixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(target))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(sourceBitmap, 0, 0, newWidth, newHeight);
        }

        var output = new byte[newWidth * newHeight * 4];
        var targetData = target.LockBits(new DrawingRect(0, 0, newWidth, newHeight),
            DrawingImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(targetData.Scan0, output, 0, output.Length);
        target.UnlockBits(targetData);

        // GIF yolunda tüm pikseller opak olmalı.
        for (int i = 3; i < output.Length; i += 4) output[i] = 255;
        return output;
    }
}
