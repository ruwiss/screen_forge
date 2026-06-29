using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenForge.Upload;

/// <summary>
/// Görüntüleri Lightshot'un prntscr.com upload backend'ine yükler (resmi olmayan API).
/// Verilen TS spec'inin birebir C# uyarlaması.
/// </summary>
public sealed class PrntscrUploadProvider : IUploadProvider
{
    private static readonly string PrntscrSecret =
        Environment.GetEnvironmentVariable("SCREENFORGE_UPLOAD_SECRET") ?? "";

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public async Task<UploadResult> UploadAsync(byte[] imageBytes, string mimeType, Action<double>? progress = null)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Geçersiz görüntü verisi.");

        progress?.Invoke(0.05);

        string ext = MimeToExtension(mimeType);
        var (width, height) = ReadPngDimensions(imageBytes); // PNG değilse 1×1

        // İmzalı yükleme URL'si
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string hash = Md5Hex(PrntscrSecret + timestamp);
        string uploadUrl = $"https://upload.prntscr.com/upload/{timestamp}/{hash}/";

        progress?.Invoke(0.15);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(width.ToString()), "width" },
            { new StringContent(height.ToString()), "height" },
            { new StringContent("1.000000"), "dpi" },
            { new StringContent(Guid.NewGuid().ToString()), "app_id" },
        };
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        form.Add(imageContent, "image", $"todo-image.{ext}");

        progress?.Invoke(0.3);

        using var req = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = form };
        req.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);

        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        string xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        progress?.Invoke(0.7);

        string status = Match(xml, @"<status>(\w+)</status>");
        string shareUrl = Match(xml, @"<share>([^<]+)</share>");

        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(shareUrl))
        {
            string error = Match(xml, @"<error>([^<]+)</error>");
            throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Yükleme reddedildi." : error);
        }

        shareUrl = System.Net.WebUtility.HtmlDecode(shareUrl);
        progress?.Invoke(0.85);

        // Paylaşım sayfasından doğrudan görüntü URL'sini kazı.
        string? directUrl = await TryScrapeDirectUrl(shareUrl).ConfigureAwait(false);

        progress?.Invoke(1.0);
        return new UploadResult(directUrl ?? shareUrl, shareUrl);
    }

    private static async Task<string?> TryScrapeDirectUrl(string shareUrl)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, shareUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            // class'ında screenshot-image geçen <img> etiketi; src → ters sıra → data-src
            var patterns = new[]
            {
                @"<img[^>]*class=""[^""]*screenshot-image[^""]*""[^>]*src=""([^""]+)""",
                @"<img[^>]*src=""([^""]+)""[^>]*class=""[^""]*screenshot-image[^""]*""",
                @"<img[^>]*class=""[^""]*screenshot-image[^""]*""[^>]*data-src=""([^""]+)""",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(html, p, RegexOptions.IgnoreCase);
                if (m.Success) return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            }
        }
        catch
        {
            // İkincil çekme/kazıma başarısızsa yut.
        }
        return null;
    }

    // ---- Yardımcılar ----
    private static string MimeToExtension(string mime) => mime.ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/webp" => "webp",
        _ => "png",
    };

    private static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string Match(string input, string pattern)
    {
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>PNG imzasını doğrular ve gerçek boyutları okur; geçerli değilse 1×1.</summary>
    private static (int width, int height) ReadPngDimensions(byte[] data)
    {
        // PNG imzası: bytes 1..4 == "PNG"
        if (data.Length >= 24 &&
            data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        {
            int width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            int height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            if (width > 0 && height > 0) return (width, height);
        }
        return (1, 1);
    }
}
