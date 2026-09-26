using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ScreenForge.Translate;

namespace ScreenForge.Subtitle;

internal static class SubtitleOcr
{
    private static OcrEngine? _engine;
    private static bool _engineFailed;

    public static async Task<string?> RecognizeAsync(byte[] bgra, int width, int height, CancellationToken ct)
    {
        var (dw, dh) = SubtitleMath.OcrSize(width, height);
        byte[] pixels = dw == width && dh == height
            ? bgra
            : SubtitleMath.ScaleBgra(bgra, width, height, dw, dh);

        if (!_engineFailed)
        {
            try
            {
                string? local = await RecognizeWindowsAsync(pixels, dw, dh).ConfigureAwait(false);
                string? normalized = SubtitleMath.NormalizeOcr(local);
                if (normalized != null)
                    return normalized;
            }
            catch
            {
                _engineFailed = true;
            }
        }

        return null;
    }

    public static async Task<string?> RecognizeLensAsync(
        GoogleLensClient lens, byte[] bgra, int width, int height, CancellationToken ct)
    {
        var (dw, dh) = SubtitleMath.OcrSize(width, height);
        byte[] pixels = dw == width && dh == height
            ? bgra
            : SubtitleMath.ScaleBgra(bgra, width, height, dw, dh);

        try
        {
            byte[] png = ToPng(pixels, dw, dh);
            var result = await lens.ExtractTextAsync(png, dw, dh, ct).ConfigureAwait(false);
            return SubtitleMath.NormalizeOcr(result.OcrText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> RecognizeWindowsAsync(byte[] bgra, int width, int height)
    {
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (_engine == null)
        {
            _engineFailed = true;
            return null;
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);
        return result.Text;
    }

    private static byte[] ToPng(byte[] bgra, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), width * height * 4);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 80)
            ?? throw new InvalidOperationException("PNG kodlanamadı.");
        return data.ToArray();
    }
}
