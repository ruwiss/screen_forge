using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScreenForge.Search;

public enum ReverseSearchEngine
{
    Yandex,
    GoogleLens,
}

public sealed class ReverseImageSearch : IDisposable
{
    private const string ChromeUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly Uri YandexCom = new("https://yandex.com");
    private static readonly Uri YandexRu = new("https://yandex.ru");

    private readonly HttpClient _http;

    public ReverseImageSearch()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeUa);
    }

    public void Dispose() => _http.Dispose();

    public Task<Uri> SearchAsync(byte[] pngBytes, ReverseSearchEngine engine, CancellationToken ct)
    {
        if (pngBytes is null || pngBytes.Length == 0)
            throw new ArgumentException("Empty image.", nameof(pngBytes));

        return engine switch
        {
            ReverseSearchEngine.Yandex => SearchYandexAsync(pngBytes, ct),
            ReverseSearchEngine.GoogleLens => SearchGoogleLensAsync(pngBytes, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };
    }

    public static void OpenInDefaultBrowser(Uri url)
    {
        try
        {
            string target = url.IsFile ? url.LocalPath : url.AbsoluteUri;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Tarayıcı açılamadı.", ex);
        }
    }

    internal static string BuildLensUploadHtml(byte[] pngBytes)
    {
        long st = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string b64 = Convert.ToBase64String(pngBytes);
        var sb = new StringBuilder(b64.Length + 512);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Google Lens</title></head><body>");
        sb.Append("<form id=\"f\" method=\"POST\" enctype=\"multipart/form-data\" action=\"https://lens.google.com/v3/upload?ep=ccm&amp;s=&amp;st=");
        sb.Append(st);
        sb.Append("\"><input id=\"img\" type=\"file\" name=\"encoded_image\"></form>");
        sb.Append("<script>");
        sb.Append("const b64=\"");
        sb.Append(b64);
        sb.Append("\";const bin=atob(b64);const bytes=new Uint8Array(bin.length);");
        sb.Append("for(let i=0;i<bin.length;i++)bytes[i]=bin.charCodeAt(i);");
        sb.Append("const file=new File([bytes],\"image.png\",{type:\"image/png\"});");
        sb.Append("const dt=new DataTransfer();dt.items.add(file);");
        sb.Append("document.getElementById(\"img\").files=dt.files;");
        sb.Append("document.getElementById(\"f\").submit();");
        sb.Append("</script></body></html>");
        return sb.ToString();
    }

    internal static bool TryParseYandexCbir(string json, out string cbirId, out string? originalImageUrl)
    {
        cbirId = "";
        originalImageUrl = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blocks", out var blocks)
                || blocks.ValueKind != JsonValueKind.Array
                || blocks.GetArrayLength() == 0)
                return false;

            var first = blocks[0];
            if (!first.TryGetProperty("params", out var parms))
                return false;
            if (!parms.TryGetProperty("cbirId", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return false;

            string? id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id)) return false;
            cbirId = id;

            if (parms.TryGetProperty("originalImageUrl", out var urlEl)
                && urlEl.ValueKind == JsonValueKind.String)
            {
                string? url = urlEl.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    originalImageUrl = url;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<Uri> SearchYandexAsync(byte[] pngBytes, CancellationToken ct)
    {
        if (await TryYandexHostAsync(YandexCom, pngBytes, ct).ConfigureAwait(false) is { } com)
            return com;
        if (await TryYandexHostAsync(YandexRu, pngBytes, ct).ConfigureAwait(false) is { } ru)
            return ru;
        throw new InvalidOperationException("Yandex görsel araması başarısız.");
    }

    private async Task<Uri?> TryYandexHostAsync(Uri origin, byte[] pngBytes, CancellationToken ct)
    {
        const string requestJson = """{"blocks":[{"block":"cbir-uploader__get-cbir-id"}]}""";
        string url =
            $"{origin.GetLeftPart(UriPartial.Authority)}/images/touch/search?rpt=imageview&format=json&request={Uri.EscapeDataString(requestJson)}";

        try
        {
            using var content = new MultipartFormDataContent();
            var image = new ByteArrayContent(pngBytes);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(image, "upfile", "image.png");

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            if (!TryParseYandexCbir(body, out string cbirId, out string? originalImageUrl))
                return null;

            string result =
                $"{origin.GetLeftPart(UriPartial.Authority)}/images/search?cbir_id={Uri.EscapeDataString(cbirId)}&rpt=imageview";
            if (!string.IsNullOrWhiteSpace(originalImageUrl))
                result += "&url=" + Uri.EscapeDataString(originalImageUrl);
            return new Uri(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private Task<Uri> SearchGoogleLensAsync(byte[] pngBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge-lens-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, BuildLensUploadHtml(pngBytes), Encoding.UTF8);
        _ = DeleteLater(path);
        return Task.FromResult(new Uri(path));
    }

    private static async Task DeleteLater(string path)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            File.Delete(path);
        }
        catch
        {
            /* temp */
        }
    }
}
