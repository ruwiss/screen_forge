using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using SkiaSharp;
using ScreenForge.Settings;

namespace ScreenForge.Editor;

/// <summary>Sahneyi kodlar (PNG/JPEG/WebP), panoya kopyalar ve dosyaya kaydeder.</summary>
public static class ImageExporter
{
    // Prntscr upload testi (2026-07-07): 5,200,000 byte kabul, 5,242,200+ byte ret.
    // Sınıra yakın oynama olduğu için güvenli pay bırakılır.
    public const int UploadMaxBytes = 5_200_000;

    public static SKData Encode(SKBitmap bmp, ImageFormat format, int quality)
    {
        var (skFormat, q) = format switch
        {
            ImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100)),
            ImageFormat.Webp => (SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100)),
            _ => (SKEncodedImageFormat.Png, 100),
        };

        // JPEG saydamlığı desteklemez → beyaz zemine düzleştir.
        if (skFormat == SKEncodedImageFormat.Jpeg)
        {
            using var flat = new SKBitmap(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var c = new SKCanvas(flat))
            {
                c.Clear(SKColors.White);
                c.DrawBitmap(bmp, 0, 0);
            }
            using var img = SKImage.FromBitmap(flat);
            return img.Encode(skFormat, q);
        }
        else
        {
            using var img = SKImage.FromBitmap(bmp);
            return img.Encode(skFormat, q);
        }
    }

    public static string Extension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Webp => "webp",
        _ => "png",
    };

    public static string MimeType(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Webp => "image/webp",
        _ => "image/png",
    };

    public static (byte[] Bytes, string MimeType) EncodeForUpload(SKBitmap bmp, bool preserveTransparency = false)
    {
        using var pngData = Encode(bmp, ImageFormat.Png, 100);
        var pngBytes = pngData.ToArray();
        if (pngBytes.Length <= UploadMaxBytes)
            return (pngBytes, MimeType(ImageFormat.Png));

        if (preserveTransparency)
            throw new InvalidOperationException("Saydam PNG 5,2 MB sınırını aşıyor. Daha küçük alan seçin veya arkaplanlı yükleyin.");

        foreach (int quality in new[] { 92, 85, 78, 70, 62, 54 })
        {
            using var jpegData = Encode(bmp, ImageFormat.Jpeg, quality);
            var jpegBytes = jpegData.ToArray();
            if (jpegBytes.Length <= UploadMaxBytes)
                return (jpegBytes, MimeType(ImageFormat.Jpeg));
        }

        throw new InvalidOperationException("Yükleme için görüntü 5,2 MB sınırını aşıyor. Daha küçük alan seçin veya kırparak yükleyin.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint hMem);

    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint CF_DIBV5 = 17;

    /// <summary>Win32 clipboard ile CF_DIBV5 (düz alfa) + PNG / image/png koyar.</summary>
    public static void CopyToClipboard(SKBitmap bmp)
    {
        if (!OpenClipboard(nint.Zero))
            throw new InvalidOperationException("Pano açılamadı.");
        try
        {
            EmptyClipboard();
            SetDibV5(bmp);
            using var pngData = Encode(bmp, ImageFormat.Png, 100);
            var pngBytes = pngData.ToArray();
            SetBytesFormat("PNG", pngBytes);
            SetBytesFormat("image/png", pngBytes);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Panodan görüntü okur; PNG (alfa) tercih edilir. WPF GetImage DIB'i beyaza düzler.
    /// </summary>
    public static SKBitmap? TryGetClipboardBitmap()
    {
        if (!OpenClipboard(nint.Zero))
            return null;
        try
        {
            foreach (string name in new[] { "PNG", "image/png" })
            {
                uint fmt = RegisterClipboardFormatW(name);
                if (fmt == 0) continue;
                var hMem = GetClipboardData(fmt);
                if (hMem == nint.Zero) continue;
                nuint size = GlobalSize(hMem);
                if (size == 0 || size > 64 * 1024 * 1024) continue;
                var ptr = GlobalLock(hMem);
                if (ptr == nint.Zero) continue;
                try
                {
                    var bytes = new byte[(int)size];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                    var decoded = SKBitmap.Decode(bytes);
                    if (decoded != null)
                        return decoded;
                }
                finally
                {
                    GlobalUnlock(hMem);
                }
            }
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static unsafe void SetDibV5(SKBitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int stride = w * 4;
        int pixelSize = stride * h;
        int headerSize = 124; // BITMAPV5HEADER

        var hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)(headerSize + pixelSize));
        if (hMem == nint.Zero) return;
        var ptr = GlobalLock(hMem);
        if (ptr == nint.Zero) return;

        try
        {
            var span = new Span<byte>((void*)ptr, headerSize + pixelSize);
            span.Clear();
            using var writer = new MemoryStream(headerSize);
            using var bw = new BinaryWriter(writer);

            bw.Write(headerSize);       // bV5Size
            bw.Write(w);                // bV5Width
            bw.Write(h);                // bV5Height (positive = bottom-up)
            bw.Write((short)1);         // bV5Planes
            bw.Write((short)32);        // bV5BitCount
            bw.Write(3);                // bV5Compression = BI_BITFIELDS
            bw.Write(pixelSize);        // bV5SizeImage
            bw.Write(0);                // bV5XPelsPerMeter
            bw.Write(0);                // bV5YPelsPerMeter
            bw.Write(0);                // bV5ClrUsed
            bw.Write(0);                // bV5ClrImportant
            bw.Write(0x00FF0000);       // bV5RedMask
            bw.Write(0x0000FF00);       // bV5GreenMask
            bw.Write(0x000000FF);       // bV5BlueMask
            bw.Write(unchecked((int)0xFF000000)); // bV5AlphaMask
            bw.Write(0x73524742);       // bV5CSType = "sRGB"
            bw.Write(new byte[36]);     // bV5Endpoints (CIEXYZTRIPLE)
            bw.Write(0);                // bV5GammaRed
            bw.Write(0);                // bV5GammaGreen
            bw.Write(0);               // bV5GammaBlue
            bw.Write(0);                // bV5Intent = LCS_GM_IMAGES
            bw.Write(0);                // bV5ProfileData
            bw.Write(0);               // bV5ProfileSize
            bw.Write(0);                // bV5Reserved

            writer.ToArray().CopyTo(span);

            // Pixel data — bottom-up + düz (unpremultiplied) alfa.
            // Premul DIBV5 birçok uygulamada şeffaf bölgeyi beyaz/siyah gösterir.
            CopyUnpremultipliedBgraBottomUp(bmp, span.Slice(headerSize));
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        SetClipboardData(CF_DIBV5, hMem);
    }

    /// <summary>Premul BGRA → düz BGRA, satırlar alttan üste (DIB).</summary>
    internal static void CopyUnpremultipliedBgraBottomUp(SKBitmap bmp, Span<byte> dest)
    {
        int w = bmp.Width, h = bmp.Height;
        int stride = w * 4;
        var src = bmp.GetPixelSpan();
        var srcBytes = MemoryMarshal.AsBytes(src);
        for (int y = 0; y < h; y++)
        {
            var srcRow = srcBytes.Slice((h - 1 - y) * stride, stride);
            var dstRow = dest.Slice(y * stride, stride);
            UnpremultiplyBgraRow(srcRow, dstRow);
        }
    }

    internal static void UnpremultiplyBgraRow(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int i = 0; i < src.Length; i += 4)
        {
            byte b = src[i], g = src[i + 1], r = src[i + 2], a = src[i + 3];
            if (a is 0 or 255)
            {
                dst[i] = b;
                dst[i + 1] = g;
                dst[i + 2] = r;
                dst[i + 3] = a;
                continue;
            }

            dst[i] = (byte)Math.Min(255, (b * 255 + a / 2) / a);
            dst[i + 1] = (byte)Math.Min(255, (g * 255 + a / 2) / a);
            dst[i + 2] = (byte)Math.Min(255, (r * 255 + a / 2) / a);
            dst[i + 3] = a;
        }
    }

    private static void SetBytesFormat(string formatName, byte[] bytes)
    {
        uint fmt = RegisterClipboardFormatW(formatName);
        if (fmt == 0) return;

        var hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        if (hMem == nint.Zero) return;
        var ptr = GlobalLock(hMem);
        if (ptr == nint.Zero) return;

        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        SetClipboardData(fmt, hMem);
    }

    /// <summary>Dosyaya kaydeder, tam yolu döner.</summary>
    public static string SaveToFile(SKBitmap bmp, string directory, ImageFormat format, int quality)
    {
        Directory.CreateDirectory(directory);
        string name = $"ScreenForge_{DateTime.Now:yyyyMMdd_HHmmss}.{Extension(format)}";
        string path = Path.Combine(directory, name);
        using var data = Encode(bmp, format, quality);
        SaveEncoded(data, path);
        return path;
    }

    internal static void SaveEncoded(SKData data, string path)
    {
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>Sahneyi kodlanmış byte dizisine çevirir (yükleme için).</summary>
    public static byte[] EncodeScene(Scene scene, ImageFormat format, int quality)
    {
        using var bmp = SceneRenderer.RenderToBitmap(scene);
        using var data = Encode(bmp, format, quality);
        return data.ToArray();
    }
}
