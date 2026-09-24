using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScreenForge.Translate;

/// <summary>
/// Reverse-engineered Google Translate text client (Chrome public keys, no cookies).
/// Chain: pa-html → pa-gtx → dict-chrome-ex → gtx.
/// </summary>
public sealed class GoogleTextTranslateClient
{
    private const string ChromeUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>Public Chrome Translate Element / translateHtml key.</summary>
    internal const string KeyHtml = "AIzaSyATBXajvzQLTDHEQbcpq0Ihe0vWDHmO520";

    /// <summary>Public Chrome translate-pa gtx key.</summary>
    internal const string KeyPa = "AIzaSyDLEeFI5OtFBwYBIoK_jj5m32rZK5CkCXA";

    private const int CacheCap = 256;

    private static readonly HttpClient Http = CreateHttp();
    private static readonly Dictionary<string, TranslateResult> Cache = new();
    private static readonly object CacheLock = new();

    private static HttpClient CreateHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
        };
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ChromeUa);
        return http;
    }

    /// <summary>Translate <paramref name="text"/> into <paramref name="targetLang"/> (source auto-detected).</summary>
    public async Task<TranslateResult?> TranslateAsync(
        string text,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        text = NormalizeBreaks(text).Trim();
        targetLang = targetLang.Trim();
        if (text.Length == 0 || targetLang.Length == 0)
            return null;

        string cacheKey = targetLang + "\0" + text;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var hit))
                return hit;
        }

        TranslateResult? result = text.Contains('\n')
            ? await TranslateMultilineAsync(text, targetLang, cancellationToken).ConfigureAwait(false)
            : await TranslateSingleAsync(text, targetLang, cancellationToken).ConfigureAwait(false);
        if (result != null)
            result = result.Value with { Text = NormalizeBreaks(result.Value.Text).Trim('\n') };

        if (result == null)
            return null;

        lock (CacheLock)
        {
            if (Cache.Count >= CacheCap)
                Cache.Clear();
            Cache[cacheKey] = result.Value;
        }

        return result;
    }

    private static async Task<TranslateResult?> TranslateSingleAsync(
        string text, string targetLang, CancellationToken ct)
        => await TryTranslateHtmlAsync(text, targetLang, ct).ConfigureAwait(false)
           ?? await TryTranslatePaAsync(text, targetLang, ct).ConfigureAwait(false)
           ?? await TryDictChromeAsync(text, targetLang, ct).ConfigureAwait(false)
           ?? await TryGtxAsync(text, targetLang, ct).ConfigureAwait(false);

    private async Task<TranslateResult?> TranslateMultilineAsync(
        string text, string targetLang, CancellationToken ct)
    {
        var jobs = new List<(int Index, string Text)>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length > 0)
                jobs.Add((i, trimmed));
        }
        if (jobs.Count == 0)
            return null;

        var translated = new string?[jobs.Count];
        string sourceLang = "";
        int cursor = 0;
        while (cursor < jobs.Count)
        {
            int take = 0;
            int chars = 0;
            while (cursor + take < jobs.Count && take < 40 && chars < 4000)
            {
                chars += jobs[cursor + take].Text.Length + 1;
                take++;
            }

            string[] slice = new string[take];
            for (int i = 0; i < take; i++)
                slice[i] = jobs[cursor + i].Text;

            var batch = await TryTranslateHtmlLinesAsync(slice, targetLang, ct).ConfigureAwait(false);
            if (batch == null || batch.Value.Texts.Length != take)
                break;

            for (int i = 0; i < take; i++)
                translated[cursor + i] = batch.Value.Texts[i];
            if (sourceLang.Length == 0)
                sourceLang = batch.Value.SourceLang;
            cursor += take;
        }

        for (int i = cursor; i < jobs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var one = await TranslateSingleAsync(jobs[i].Text, targetLang, ct).ConfigureAwait(false);
            translated[i] = one?.Text;
            if (sourceLang.Length == 0 && one != null)
                sourceLang = one.Value.SourceLang;
        }

        string? joined = JoinPreservingBlanks(text, translated);
        if (joined == null)
            return null;
        return new TranslateResult(joined, sourceLang);
    }

    internal static string NormalizeBreaks(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);

    internal static string? JoinPreservingBlanks(string normalized, IReadOnlyList<string?> translatedNonEmpty)
    {
        string[] lines = normalized.Split('\n');
        int expected = 0;
        foreach (string line in lines)
        {
            if (line.Trim().Length > 0)
                expected++;
        }
        if (expected != translatedNonEmpty.Count || expected == 0)
            return null;

        var sb = new StringBuilder();
        int k = 0;
        bool any = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                sb.Append('\n');
            if (lines[i].Trim().Length == 0)
                continue;
            string piece = (translatedNonEmpty[k++] ?? "").Trim();
            if (piece.Length == 0)
                piece = lines[i].Trim();
            else
                any = true;
            sb.Append(piece);
        }
        return any ? sb.ToString() : null;
    }

    private static async Task<TranslateResult?> TryTranslateHtmlAsync(
        string text, string targetLang, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(new object[]
        {
            new object[] { new[] { text }, "auto", targetLang },
            "te_lib",
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://translate-pa.googleapis.com/v1/translateHtml");
        req.Content = new StringContent(payload, Encoding.UTF8);
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json+protobuf");
        req.Headers.TryAddWithoutValidation("X-Goog-Api-Key", KeyHtml);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Origin", "https://translate.google.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://translate.google.com/");

        return await SendAndParseAsync(req, ParseTranslateHtml, ct).ConfigureAwait(false);
    }

    private static async Task<(string[] Texts, string SourceLang)?> TryTranslateHtmlLinesAsync(
        string[] lines, string targetLang, CancellationToken ct)
    {
        if (lines.Length == 0)
            return null;

        string payload = JsonSerializer.Serialize(new object[]
        {
            new object[] { lines, "auto", targetLang },
            "te_lib",
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://translate-pa.googleapis.com/v1/translateHtml");
        req.Content = new StringContent(payload, Encoding.UTF8);
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json+protobuf");
        req.Headers.TryAddWithoutValidation("X-Goog-Api-Key", KeyHtml);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Origin", "https://translate.google.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://translate.google.com/");

        try
        {
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            string[]? texts = ParseTranslateHtmlSegments(raw, out string sourceLang);
            if (texts == null || texts.Length != lines.Length)
                return null;
            return (texts, sourceLang);
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

    private static async Task<TranslateResult?> TryTranslatePaAsync(
        string text, string targetLang, CancellationToken ct)
    {
        string url =
            "https://translate-pa.googleapis.com/v1/translate"
            + "?params.client=gtx"
            + "&query.source_language=auto"
            + "&query.target_language=" + Uri.EscapeDataString(targetLang)
            + "&query.text=" + Uri.EscapeDataString(text)
            + "&key=" + Uri.EscapeDataString(KeyPa)
            + "&data_types=TRANSLATION"
            + "&data_types=SENTENCE_SPLITS";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Referer", "https://translate.google.com/");

        return await SendAndParseAsync(req, ParseTranslatePa, ct).ConfigureAwait(false);
    }

    private static async Task<TranslateResult?> TryDictChromeAsync(
        string text, string targetLang, CancellationToken ct)
    {
        string url =
            "https://clients5.google.com/translate_a/t"
            + "?client=dict-chrome-ex"
            + "&sl=auto"
            + "&tl=" + Uri.EscapeDataString(targetLang)
            + "&q=" + Uri.EscapeDataString(text);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Referer", "https://translate.google.com/");

        return await SendAndParseAsync(req, ParseDictChrome, ct).ConfigureAwait(false);
    }

    private static async Task<TranslateResult?> TryGtxAsync(
        string text, string targetLang, CancellationToken ct)
    {
        string url =
            "https://translate.googleapis.com/translate_a/single"
            + "?client=gtx"
            + "&sl=auto"
            + "&tl=" + Uri.EscapeDataString(targetLang)
            + "&dt=t"
            + "&q=" + Uri.EscapeDataString(text);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Referer", "https://translate.google.com/");

        return await SendAndParseAsync(req, ParseGtx, ct).ConfigureAwait(false);
    }

    private static async Task<TranslateResult?> SendAndParseAsync(
        HttpRequestMessage req,
        Func<string, TranslateResult?> parse,
        CancellationToken ct)
    {
        try
        {
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return parse(raw);
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

    /// <summary>Body: <c>[["translated"],["ja"]]</c></summary>
    internal static TranslateResult? ParseTranslateHtml(string raw)
    {
        if (!TryParseJson(raw, out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var texts = root[0];
            if (texts.ValueKind != JsonValueKind.Array)
                return null;

            var sb = new StringBuilder();
            foreach (var item in texts.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    sb.Append(item.GetString());
            }

            string text = UnescapeHtml(sb.ToString().Trim());
            if (text.Length == 0)
                return null;

            string sourceLang = "";
            if (root.GetArrayLength() > 1
                && root[1].ValueKind == JsonValueKind.Array
                && root[1].GetArrayLength() > 0
                && root[1][0].ValueKind == JsonValueKind.String)
            {
                sourceLang = root[1][0].GetString() ?? "";
            }

            return new TranslateResult(text, sourceLang);
        }
    }

    internal static string[]? ParseTranslateHtmlSegments(string raw, out string sourceLang)
    {
        sourceLang = "";
        if (!TryParseJson(raw, out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var texts = root[0];
            if (texts.ValueKind != JsonValueKind.Array || texts.GetArrayLength() == 0)
                return null;

            var list = new List<string>();
            foreach (var item in texts.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(UnescapeHtml(item.GetString() ?? "").Trim());
                else if (item.ValueKind == JsonValueKind.Array
                         && item.GetArrayLength() > 0
                         && item[0].ValueKind == JsonValueKind.String)
                    list.Add(UnescapeHtml(item[0].GetString() ?? "").Trim());
                else
                    return null;
            }

            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var lang in root[1].EnumerateArray())
                {
                    if (lang.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(lang.GetString()))
                    {
                        sourceLang = lang.GetString() ?? "";
                        break;
                    }
                }
            }
            else if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.String)
            {
                sourceLang = root[1].GetString() ?? "";
            }

            return list.ToArray();
        }
    }

    /// <summary>JSON: <c>{ "translation": "...", "sourceLanguage": "ja" }</c></summary>
    internal static TranslateResult? ParseTranslatePa(string raw)
    {
        if (!TryParseJson(raw, out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty("translation", out var tEl) || tEl.ValueKind != JsonValueKind.String)
                return null;

            string text = (tEl.GetString() ?? "").Trim();
            if (text.Length == 0)
                return null;

            string sourceLang = "";
            if (root.TryGetProperty("sourceLanguage", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                sourceLang = sEl.GetString() ?? "";

            return new TranslateResult(text, sourceLang);
        }
    }

    /// <summary>JSON: <c>[["translated","ja"]]</c> or rarely <c>["translated","ja"]</c></summary>
    internal static TranslateResult? ParseDictChrome(string raw)
    {
        if (!TryParseJson(raw, out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var first = root[0];
            if (first.ValueKind == JsonValueKind.Array)
            {
                if (first.GetArrayLength() == 0 || first[0].ValueKind != JsonValueKind.String)
                    return null;
                string text = (first[0].GetString() ?? "").Trim();
                if (text.Length == 0)
                    return null;
                string sourceLang = "";
                if (first.GetArrayLength() > 1 && first[1].ValueKind == JsonValueKind.String)
                    sourceLang = first[1].GetString() ?? "";
                return new TranslateResult(text, sourceLang);
            }

            if (first.ValueKind != JsonValueKind.String)
                return null;
            string flat = (first.GetString() ?? "").Trim();
            if (flat.Length == 0)
                return null;
            string src = "";
            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.String)
                src = root[1].GetString() ?? "";
            return new TranslateResult(flat, src);
        }
    }

    /// <summary>JSON: <c>[[["translated","src",...]],null,"ja"]</c></summary>
    internal static TranslateResult? ParseGtx(string raw)
    {
        if (!TryParseJson(raw, out var doc))
            return null;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var segments = root[0];
            if (segments.ValueKind != JsonValueKind.Array)
                return null;

            var sb = new StringBuilder();
            foreach (var seg in segments.EnumerateArray())
            {
                if (seg.ValueKind == JsonValueKind.Array
                    && seg.GetArrayLength() > 0
                    && seg[0].ValueKind == JsonValueKind.String)
                {
                    sb.Append(seg[0].GetString());
                }
            }

            string text = sb.ToString().Trim();
            if (text.Length == 0)
                return null;

            string sourceLang = "";
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                sourceLang = root[2].GetString() ?? "";

            return new TranslateResult(text, sourceLang);
        }
    }

    internal static string UnescapeHtml(string s)
    {
        if (!s.Contains('&'))
            return s;
        return s
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'")
            .Replace("&nbsp;", " ");
    }

    private static bool TryParseJson(string raw, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(raw);
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }
}

/// <summary>Translated text plus Google's detected source language (empty if unknown).</summary>
public readonly record struct TranslateResult(string Text, string SourceLang);
