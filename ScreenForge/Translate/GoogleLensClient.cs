using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace ScreenForge.Translate;

/// <summary>
/// Google Lens crupload client (Chromium frontend). No Botguard / no browser.
/// Endpoint/API key: SCREENFORGE_LENS_ENDPOINT, SCREENFORGE_LENS_API_KEY.
/// </summary>
public sealed class GoogleLensClient : IDisposable
{
    public const string DefaultEndpoint = "https://lensfrontend-pa.googleapis.com/v1/crupload";
    public const string DefaultApiKey = "AIzaSyDr2UxVnv_U85AbhhY8XSHSIavUW0DC-sY";

    private static readonly string[] FallbackKeys =
    [
        Environment.GetEnvironmentVariable("SCREENFORGE_LENS_API_KEY") ?? "",
        DefaultApiKey,
    ];

    private readonly HttpClient _http;

    public GoogleLensClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public void Dispose() => _http.Dispose();

    public async Task<LensTranslateResult> TranslateImageAsync(
        byte[] pngBytes,
        int width,
        int height,
        string targetLanguage = "tr",
        string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        if (pngBytes.Length == 0) throw new ArgumentException("Empty image.", nameof(pngBytes));
        if (width <= 0 || height <= 0) throw new ArgumentException("Invalid dimensions.");

        string endpoint = Environment.GetEnvironmentVariable("SCREENFORGE_LENS_ENDPOINT")
            ?? DefaultEndpoint;

        var keys = FallbackKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();
        if (keys.Length == 0) keys = [DefaultApiKey];

        Exception? last = null;
        foreach (var key in keys)
        {
            try
            {
                byte[] payload = BuildRequest(pngBytes, width, height, targetLanguage, sourceLanguage);
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    Content = new ByteArrayContent(payload),
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
                req.Headers.TryAddWithoutValidation("X-Goog-Api-Key", key);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                byte[] body = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    last = new HttpRequestException(
                        $"Lens HTTP {(int)resp.StatusCode}: {Encoding.UTF8.GetString(body.AsSpan(0, Math.Min(body.Length, 200)))}");
                    continue;
                }

                var parsed = ProtoResponseParser.Parse(body, targetLanguage);
                bool hasContent = parsed.Blocks.Count > 0
                    || !string.IsNullOrWhiteSpace(parsed.TranslatedText)
                    || !string.IsNullOrWhiteSpace(parsed.OcrText);
                if (!hasContent)
                {
                    last = new InvalidOperationException("Lens returned empty OCR/translation.");
                    continue;
                }

                return parsed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Görüntü çevirisi başarısız. Ağ bağlantısını kontrol edin veya daha sonra tekrar deneyin.", last);
    }

    internal static byte[] BuildRequest(
        byte[] imageBytes, int width, int height, string targetLang, string? sourceLang)
    {
        const int PlatformWeb = 3;
        const int SurfaceChromium = 4;
        const int FilterTranslate = 2;

        ulong uuid = (ulong)Random.Shared.NextInt64(1, long.MaxValue);

        var root = new ProtoWriter();
        root.WriteMessage(1, objects =>
        {
            objects.WriteMessage(1, ctx =>
            {
                ctx.WriteMessage(3, rid =>
                {
                    rid.WriteUInt64(1, uuid);
                    rid.WriteInt32(2, 1);
                    rid.WriteInt32(3, 1);
                });
                ctx.WriteMessage(4, client =>
                {
                    client.WriteEnum(1, PlatformWeb);
                    client.WriteEnum(2, SurfaceChromium);
                    client.WriteMessage(4, locale =>
                    {
                        locale.WriteString(2, "US");
                        locale.WriteString(3, "America/New_York");
                    });
                    client.WriteMessage(17, filters =>
                    {
                        filters.WriteMessage(1, filter =>
                        {
                            filter.WriteEnum(1, FilterTranslate);
                            filter.WriteMessage(3, tr =>
                            {
                                tr.WriteString(1, targetLang);
                                if (!string.IsNullOrWhiteSpace(sourceLang))
                                    tr.WriteString(2, sourceLang);
                            });
                        });
                    });
                });
            });
            objects.WriteMessage(3, image =>
            {
                image.WriteMessage(1, payload => payload.WriteBytes(1, imageBytes));
                image.WriteMessage(3, meta =>
                {
                    meta.WriteInt32(1, width);
                    meta.WriteInt32(2, height);
                });
            });
        });
        return root.ToArray();
    }
}

public sealed class LensTranslateResult
{
    public string? OcrText { get; init; }
    public string? TranslatedText { get; init; }
    public string? DetectedLanguage { get; init; }
    public IReadOnlyList<LensWordBox> Words { get; init; } = Array.Empty<LensWordBox>();
    /// <summary>Paragraph-aligned OCR + translation blocks with normalized geometry.</summary>
    public IReadOnlyList<LensTextBlock> Blocks { get; init; } = Array.Empty<LensTextBlock>();
}

public readonly record struct LensWordBox(
    string Text,
    float CenterX, float CenterY, float Width, float Height);

public readonly record struct LensNormBox(
    float CenterX, float CenterY, float Width, float Height);

/// <param name="ShouldReplace">True when we should cover original text and draw translation.</param>
public sealed record LensTextBlock(
    string OcrText,
    string TranslatedText,
    LensNormBox Box,
    int StatusCode,
    bool ShouldReplace);
