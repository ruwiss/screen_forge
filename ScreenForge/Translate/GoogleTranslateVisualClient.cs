using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SkiaSharp;

namespace ScreenForge.Translate;

/// <summary>
/// Google Translate web Images — sitenin ürettiği çevrilmiş PNG.
/// WebView2 gerçek tarayıcı motoru (Botguard dahil) kullanır.
/// Hız: ısınma + sayfa yeniden kullanımı + blob-öncelikli alma + kısa poll.
/// </summary>
public sealed class GoogleTranslateVisualClient : IAsyncDisposable
{
    private Window? _host;
    private WebView2? _web;
    private string? _warmedSl;
    private string? _warmedTl;
    private bool _pageReady;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Task RunOnUiAsync(Func<Task> work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF Application yok.");
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await work().ConfigureAwait(true);
                tcs.TrySetResult();
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }, DispatcherPriority.Normal);
        return tcs.Task;
    }

    /// <summary>WebView2 + Images sayfasını önceden ısıtır (ilk Çevir’i hızlandırır).</summary>
    public async Task WarmupAsync(string targetLang = "tr", string? sourceLang = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            await EnsureReadyAsync(NormalizeSl(sourceLang), NormalizeTl(targetLang), preNavigate: true, ct)
                .ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string NormalizeSl(string? sourceLang)
        => string.IsNullOrWhiteSpace(sourceLang) || sourceLang == "auto" ? "auto" : sourceLang.Trim();

    private static string NormalizeTl(string? targetLang)
        => string.IsNullOrWhiteSpace(targetLang) ? "tr" : targetLang.Trim();

    private async Task EnsureReadyAsync(string sl, string tl, bool preNavigate, CancellationToken ct)
    {
        await RunOnUiAsync(async () =>
        {
            if (_web?.CoreWebView2 == null)
            {
                _host = new Window
                {
                    Title = "ScreenForge Translate Engine",
                    Width = 1600,
                    Height = 1000,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.ToolWindow,
                    Left = -32000,
                    Top = -32000,
                    Opacity = 0.01,
                };
                _web = new WebView2 { ZoomFactor = 1.0 };
                _host.Content = _web;
                _host.Show();
                await _web.EnsureCoreWebView2Async().ConfigureAwait(true);
                _web.ZoomFactor = 1.0;
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.Settings.IsZoomControlEnabled = false;
                try { _web.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark; } catch { }
            }

            if (preNavigate && _web.CoreWebView2 != null && (!_pageReady || _warmedTl != tl || _warmedSl != sl))
            {
                string warm = BuildPageUrl(sl, tl);
                await NavigateAsync(_web.CoreWebView2, warm, ct).ConfigureAwait(true);
                await WaitForFileInputAsync(_web.CoreWebView2, ct, maxMs: 4000).ConfigureAwait(true);
                _warmedSl = sl;
                _warmedTl = tl;
                _pageReady = true;
            }
        }).WaitAsync(ct).ConfigureAwait(true);
    }

    private static string BuildPageUrl(string sl, string tl)
        => $"https://translate.google.com/?sl={Uri.EscapeDataString(sl)}&tl={Uri.EscapeDataString(tl)}&op=images&hl=en";

    public async Task<byte[]> TranslatePngAsync(
        byte[] pngBytes,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        string sl = NormalizeSl(sourceLang);
        string tl = NormalizeTl(targetLang);

        await _gate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            await EnsureReadyAsync(sl, tl, preNavigate: true, ct).ConfigureAwait(true);

            // Beklenen boyut (UI dışında, hızlı)
            int expectW = 0, expectH = 0;
            try
            {
                var info = SKCodec.Create(new MemoryStream(pngBytes));
                if (info != null)
                {
                    expectW = info.Info.Width;
                    expectH = info.Info.Height;
                    info.Dispose();
                }
            }
            catch
            {
                try
                {
                    using var probe = SKBitmap.Decode(pngBytes);
                    if (probe != null) { expectW = probe.Width; expectH = probe.Height; }
                }
                catch { /* ignore */ }
            }

            string b64 = Convert.ToBase64String(pngBytes);
            string tempOut = Path.Combine(Path.GetTempPath(), $"sf-gt-{Guid.NewGuid():N}.png");
            byte[]? resultBytes = null;

            await RunOnUiAsync(async () =>
            {
                var core = _web?.CoreWebView2
                    ?? throw new InvalidOperationException("WebView2 hazır değil.");

                var downloadTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

                void OnDownload(object? s, CoreWebView2DownloadStartingEventArgs e)
                {
                    try
                    {
                        e.ResultFilePath = tempOut;
                        e.Handled = true;
                        e.DownloadOperation.StateChanged += (_, _) =>
                        {
                            var st = e.DownloadOperation.State;
                            if (st == CoreWebView2DownloadState.Completed)
                            {
                                try { downloadTcs.TrySetResult(File.ReadAllBytes(tempOut)); }
                                catch (Exception ex) { downloadTcs.TrySetException(ex); }
                            }
                            else if (st == CoreWebView2DownloadState.Interrupted)
                            {
                                downloadTcs.TrySetException(new IOException(
                                    "İndirme kesildi: " + e.DownloadOperation.InterruptReason));
                            }
                        };
                    }
                    catch (Exception ex) { downloadTcs.TrySetException(ex); }
                }

                core.DownloadStarting += OnDownload;
                try
                {
                    // Hızlı yol: ısınmış Images sayfasını temizle + enjekte.
                    // Başarısızsa tek seferlik taze navigate.
                    bool injected = await TryClearAndInjectAsync(core, b64, ct).ConfigureAwait(true);
                    if (!injected)
                    {
                        string navUrl = BuildPageUrl(sl, tl) + "&_sf=" + Guid.NewGuid().ToString("N")[..8];
                        await NavigateAsync(core, navUrl, ct).ConfigureAwait(true);
                        await WaitForFileInputAsync(core, ct, maxMs: 5000).ConfigureAwait(true);
                        _warmedSl = sl;
                        _warmedTl = tl;
                        _pageReady = true;

                        string injectResult = await core.ExecuteScriptAsync(BuildInjectScript(b64)).ConfigureAwait(true);
                        string injectStatus = UnwrapJsString(injectResult);
                        if (injectStatus.StartsWith("err:", StringComparison.Ordinal) || injectStatus == "no-input")
                            throw new InvalidOperationException("Google Translate dosya alanı bulunamadı: " + injectStatus);
                    }

                    // Çeviri hazır mı? (sık poll — 120ms)
                    bool ready = false;
                    bool hasDownloadBtn = false;
                    int lastBlobCount = 0;
                    int stableTicks = 0;
                    int lastMaxA = 0;
                    for (int i = 0; i < 100 && !ct.IsCancellationRequested; i++)
                    {
                        var state = await PollReadyStateAsync(core).ConfigureAwait(true);
                        if (state != null)
                        {
                            hasDownloadBtn = state.Value.hasDl;
                            if (state.Value.blobs == lastBlobCount && state.Value.maxA == lastMaxA && state.Value.blobs >= 1)
                                stableTicks++;
                            else
                            {
                                lastBlobCount = state.Value.blobs;
                                lastMaxA = state.Value.maxA;
                                stableTicks = 0;
                            }

                            // hasDl + blob: 1 tick stabil yeter (eski: 2 tick + i>=5 + 300ms ≈ yavaş)
                            if (state.Value.hasDl && state.Value.blobs >= 1 && stableTicks >= 1 && i >= 2)
                            {
                                ready = true;
                                break;
                            }
                            // 2 blob stabil
                            if (state.Value.blobs >= 2 && stableTicks >= 2 && i >= 3)
                            {
                                ready = true;
                                break;
                            }
                            // son çare
                            if (state.Value.blobs >= 1 && i >= 20 && stableTicks >= 3)
                            {
                                ready = true;
                                break;
                            }
                        }

                        await Task.Delay(120, ct).ConfigureAwait(true);
                    }

                    if (!ready)
                        throw new TimeoutException("Google Translate görseli hazır olmadı (zaman aşımı).");

                    // Kısa oturma — 900ms yerine 200ms (native testte kalite yeterli)
                    await Task.Delay(200, ct).ConfigureAwait(true);

                    // Blob önce (hızlı). İndirme paralel; blob yeterliyse indirmeyi bekleme.
                    var blobTask = TryFetchBestBlobAsync(core, expectW, expectH, ct);

                    if (hasDownloadBtn)
                    {
                        _ = core.ExecuteScriptAsync(
                            """
                            (() => {
                              const btns = [...document.querySelectorAll('button[aria-label], a[aria-label], [role=button], button, a')];
                              const b = btns.find(x => {
                                const a = (x.getAttribute('aria-label')||'').toLowerCase();
                                const t = (x.textContent||'').toLowerCase();
                                return a.includes('download translation') || a.includes('çeviriyi indir')
                                  || (a.includes('download') && (a.includes('translat') || a.includes('çevir')))
                                  || (t.includes('download') && t.includes('translat'));
                              });
                              if (b) { b.click(); return 'clicked'; }
                              return 'missing';
                            })()
                            """);
                    }

                    byte[]? blobBytes = await blobTask.ConfigureAwait(true);
                    byte[]? downloadBytes = null;

                    if (IsGoodResult(blobBytes, expectW, expectH))
                    {
                        resultBytes = blobBytes;
                    }
                    else
                    {
                        // Blob zayıfsa indirmeyi kısa süre bekle
                        if (hasDownloadBtn)
                        {
                            var raced = await Task.WhenAny(downloadTcs.Task, Task.Delay(5000, ct)).ConfigureAwait(true);
                            if (raced == downloadTcs.Task && downloadTcs.Task.IsCompletedSuccessfully)
                                downloadBytes = downloadTcs.Task.Result;
                        }
                        resultBytes = PickBestTranslationImage(downloadBytes, blobBytes, expectW, expectH);
                    }

                    // Sonraki çeviri için sayfa “kullanılmış” ama reuse edilebilir
                    _pageReady = true;
                }
                finally
                {
                    core.DownloadStarting -= OnDownload;
                    try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                }
            }).WaitAsync(ct).ConfigureAwait(true);

            if (resultBytes == null || resultBytes.Length < 32)
                throw new InvalidOperationException("Boş çeviri görüntüsü.");
            return resultBytes;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<(int blobs, bool hasDl, int maxA)?> PollReadyStateAsync(CoreWebView2 core)
    {
        string stateEnc = await core.ExecuteScriptAsync(
            """
            (() => {
              const blobs = [...document.images].filter(im =>
                im.src.startsWith('blob:') && im.naturalWidth > 40 && im.complete);
              let maxA = 0;
              for (const im of blobs) maxA = Math.max(maxA, im.naturalWidth * im.naturalHeight);
              const labels = [...document.querySelectorAll('button[aria-label], a[aria-label], [role=button]')]
                .map(b => (b.getAttribute('aria-label')||'').toLowerCase());
              const hasDl = labels.some(l =>
                l.includes('download translation') || l.includes('çeviriyi indir') ||
                (l.includes('download') && (l.includes('translat') || l.includes('çevir'))));
              return JSON.stringify({ blobs: blobs.length, hasDl, maxA });
            })()
            """).ConfigureAwait(true);

        try
        {
            string stateJson = UnwrapJsString(stateEnc);
            if (string.IsNullOrWhiteSpace(stateJson) || stateJson is "null" or "undefined")
                return null;
            using var doc = JsonDocument.Parse(stateJson);
            if (!doc.RootElement.TryGetProperty("blobs", out var blobsEl))
                return null;
            int blobs = blobsEl.GetInt32();
            bool hasDl = doc.RootElement.TryGetProperty("hasDl", out var dlEl) && dlEl.GetBoolean();
            int maxA = doc.RootElement.TryGetProperty("maxA", out var ma) ? ma.GetInt32() : 0;
            return (blobs, hasDl, maxA);
        }
        catch { return null; }
    }

    /// <summary>Önceki görseli temizle + yeni PNG enjekte et. true = inject OK.</summary>
    private static async Task<bool> TryClearAndInjectAsync(CoreWebView2 core, string b64, CancellationToken ct)
    {
        // Agresif temizle: clear düğmesi + file input reset + blob img temizliği
        await core.ExecuteScriptAsync(
            """
            (() => {
              const clr = [...document.querySelectorAll('button[aria-label], button')]
                .find(b => {
                  const a = (b.getAttribute('aria-label')||'').toLowerCase();
                  return a.includes('clear image') || a.includes('resmi temizle')
                    || a === 'clear' || a.includes('clear');
                });
              if (clr) clr.click();
              for (const input of document.querySelectorAll('input[type=file]')) {
                try {
                  input.value = '';
                  const neu = input.cloneNode(true);
                  input.parentNode && input.parentNode.replaceChild(neu, input);
                } catch (e) {}
              }
              return 'ok';
            })()
            """).ConfigureAwait(true);

        await Task.Delay(120, ct).ConfigureAwait(true);

        // File input var mı?
        bool hasInput = await WaitForFileInputAsync(core, ct, maxMs: 1500).ConfigureAwait(true);
        if (!hasInput) return false;

        string injectResult = await core.ExecuteScriptAsync(BuildInjectScript(b64)).ConfigureAwait(true);
        string injectStatus = UnwrapJsString(injectResult);
        if (injectStatus.StartsWith("err:", StringComparison.Ordinal) || injectStatus == "no-input")
            return false;

        // Enjekte sonrası kısa süre içinde blob artışı yoksa fail (eski görüntü takılı)
        await Task.Delay(200, ct).ConfigureAwait(true);
        return true;
    }

    private static async Task<bool> WaitForFileInputAsync(CoreWebView2 core, CancellationToken ct, int maxMs)
    {
        int steps = Math.Max(1, maxMs / 100);
        for (int i = 0; i < steps && !ct.IsCancellationRequested; i++)
        {
            string r = UnwrapJsString(await core.ExecuteScriptAsync(
                """
                (() => {
                  const tabs = [...document.querySelectorAll('button, [role=tab], a')];
                  const imgTab = tabs.find(el => {
                    const t = ((el.getAttribute('aria-label')||'') + ' ' + (el.textContent||'')).toLowerCase();
                    return t.includes('images') || t.includes('resim');
                  });
                  if (imgTab && imgTab.getAttribute('aria-selected') !== 'true') imgTab.click();
                  let input = document.querySelector('input[type="file"][accept*="image"]');
                  if (!input) {
                    input = [...document.querySelectorAll('input[type="file"]')]
                      .find(i => (i.accept||'').includes('image') || (i.accept||'').includes('png') || !(i.accept));
                  }
                  return input ? 'yes' : 'no';
                })()
                """).ConfigureAwait(true));
            if (r == "yes") return true;
            await Task.Delay(100, ct).ConfigureAwait(true);
        }
        return false;
    }

    private static bool IsGoodResult(byte[]? data, int expectW, int expectH)
    {
        if (data is not { Length: > 32 }) return false;
        if (expectW <= 0 || expectH <= 0) return data.Length > 1000;
        try
        {
            using var b = SKBitmap.Decode(data);
            if (b == null || b.Width < 8) return false;
            double rw = (double)b.Width / expectW;
            double rh = (double)b.Height / expectH;
            return rw is >= 0.88 and <= 1.12 && rh is >= 0.88 and <= 1.12;
        }
        catch { return false; }
    }

    private static byte[]? PickBestTranslationImage(byte[]? download, byte[]? blob, int expectW, int expectH)
    {
        long expectArea = expectW > 0 && expectH > 0 ? (long)expectW * expectH : 0;

        bool DimOk(int w, int h)
        {
            if (expectW <= 0 || expectH <= 0) return true;
            double rw = (double)w / expectW;
            double rh = (double)h / expectH;
            return rw is >= 0.88 and <= 1.12 && rh is >= 0.88 and <= 1.12;
        }

        (byte[] bytes, double score)? Score(byte[]? data, double bonus)
        {
            if (data is not { Length: > 32 }) return null;
            try
            {
                using var b = SKBitmap.Decode(data);
                if (b == null || b.Width < 8 || b.Height < 8) return null;
                long area = (long)b.Width * b.Height;
                if (expectArea > 0 && area < expectArea * 0.70)
                    return null;

                double score = bonus + Math.Log10(data.Length + 1) * 0.05;
                if (DimOk(b.Width, b.Height))
                    score += 2.0;
                else if (expectW > 0)
                {
                    double dw = Math.Abs(b.Width - expectW) / (double)expectW;
                    double dh = Math.Abs(b.Height - expectH) / (double)expectH;
                    score -= (dw + dh);
                }
                return (data, score);
            }
            catch { return null; }
        }

        var d = Score(download, bonus: 0.35);
        var l = Score(blob, bonus: 0.20);
        if (d == null && l == null) return download ?? blob;
        if (d == null) return l!.Value.bytes;
        if (l == null) return d.Value.bytes;
        return d.Value.score >= l.Value.score ? d.Value.bytes : l.Value.bytes;
    }

    private static async Task<byte[]?> TryFetchBestBlobAsync(
        CoreWebView2 core, int expectW, int expectH, CancellationToken ct)
    {
        string script = $$"""
            (async () => {
              const expectW = {{Math.Max(0, expectW)}};
              const expectH = {{Math.Max(0, expectH)}};
              const imgs = [...document.images]
                .filter(im => im.src.startsWith('blob:') && im.naturalWidth > 40 && im.complete);
              if (!imgs.length) return null;

              let candidates = imgs;
              if (expectW > 0 && expectH > 0) {
                const exact = imgs.filter(im =>
                  Math.abs(im.naturalWidth - expectW) <= Math.max(2, expectW * 0.02) &&
                  Math.abs(im.naturalHeight - expectH) <= Math.max(2, expectH * 0.02));
                if (exact.length) candidates = exact;
                else {
                  candidates = [...imgs].sort((a, b) => {
                    const da = Math.abs(a.naturalWidth - expectW) + Math.abs(a.naturalHeight - expectH);
                    const db = Math.abs(b.naturalWidth - expectW) + Math.abs(b.naturalHeight - expectH);
                    return da - db;
                  });
                  const bestD = Math.abs(candidates[0].naturalWidth - expectW) + Math.abs(candidates[0].naturalHeight - expectH);
                  candidates = candidates.filter(im =>
                    (Math.abs(im.naturalWidth - expectW) + Math.abs(im.naturalHeight - expectH)) <= bestD + 2);
                }
              }

              const img = candidates[candidates.length - 1];
              const res = await fetch(img.src);
              const blob = await res.blob();
              const buf = await blob.arrayBuffer();
              const bytes = new Uint8Array(buf);
              let bin = '';
              const chunk = 0x8000;
              for (let i = 0; i < bytes.length; i += chunk) {
                bin += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
              }
              return JSON.stringify({ b64: btoa(bin), w: img.naturalWidth, h: img.naturalHeight });
            })()
            """;

        string dataUrlEnc = await core.ExecuteScriptAsync(script).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        string? payload = UnwrapJsString(dataUrlEnc);
        if (string.IsNullOrWhiteSpace(payload) || payload is "null" or "undefined")
            return null;
        payload = UnwrapJsString(payload);
        if (payload.Length == 0 || payload[0] != '{')
            return null;
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("b64", out var b64El))
            return null;
        string? b64Out = b64El.GetString();
        if (string.IsNullOrWhiteSpace(b64Out)) return null;
        return Convert.FromBase64String(b64Out);
    }

    private static async Task NavigateAsync(CoreWebView2 core, string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= Handler;
            if (e.IsSuccess) tcs.TrySetResult();
            else tcs.TrySetException(new InvalidOperationException("Sayfa yüklenemedi."));
        }
        core.NavigationCompleted += Handler;
        core.Navigate(url);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        await tcs.Task.ConfigureAwait(true);
    }

    private static string UnwrapJsString(string executeScriptResult)
    {
        try { return JsonSerializer.Deserialize<string>(executeScriptResult) ?? executeScriptResult.Trim('"'); }
        catch { return executeScriptResult.Trim().Trim('"'); }
    }

    private static string BuildInjectScript(string pngBase64)
    {
        string safe = pngBase64.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
        return $$"""
            (() => {
              try {
                const tabs = [...document.querySelectorAll('button, [role=tab], a')];
                const imgTab = tabs.find(el => {
                  const t = ((el.getAttribute('aria-label')||'') + ' ' + (el.textContent||'')).toLowerCase();
                  return t.includes('images') || t.includes('resim');
                });
                if (imgTab) imgTab.click();

                const b64 = '{{safe}}';
                const bin = atob(b64);
                const arr = new Uint8Array(bin.length);
                for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
                const file = new File([arr], 'capture.png', { type: 'image/png' });
                const dt = new DataTransfer();
                dt.items.add(file);
                let input = document.querySelector('input[type="file"][accept*="image"]');
                if (!input) {
                  input = [...document.querySelectorAll('input[type="file"]')]
                    .find(i => (i.accept||'').includes('image') || (i.accept||'').includes('png'));
                }
                if (!input) return 'no-input';
                input.files = dt.files;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                return 'ok';
              } catch (e) { return 'err:' + e; }
            })()
            """;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RunOnUiAsync(() =>
            {
                try { _web?.Dispose(); } catch { }
                try { _host?.Close(); } catch { }
                _web = null;
                _host = null;
                _pageReady = false;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch { /* app kapanıyor olabilir */ }
        _gate.Dispose();
    }
}
