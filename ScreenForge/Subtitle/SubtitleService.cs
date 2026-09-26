using System.Drawing;
using System.Runtime.InteropServices;
using ScreenForge.Capture;
using ScreenForge.Settings;
using ScreenForge.Translate;

namespace ScreenForge.Subtitle;

internal sealed class SubtitleService : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<bool> _setChecked;
    private readonly SubtitleHitHook _hook;
    private SubtitleOverlayWindow? _overlay;
    private SubtitleRegionPicker? _picker;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _paused;
    private int _generation;
    private bool _disposed;

    public SubtitleService(Func<AppSettings> settings, Action<bool> setChecked)
    {
        _settings = settings;
        _setChecked = setChecked;
        _hook = new SubtitleHitHook(InsideBox, OnDoubleClick, OnClickOutside, OnEscape);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        var cfg = _settings();
        if (!enabled)
        {
            cfg.Subtitle.Enabled = false;
            cfg.Save();
            StopRuntime();
            _setChecked(false);
            return;
        }

        cfg.Subtitle.Enabled = true;
        cfg.Save();
        _setChecked(true);
        BeginPick(keepPrevious: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRuntime();
        _hook.Dispose();
    }

    private void StartLive()
    {
        EnsureOverlay();
        _overlay!.ShowLive();
        _hook.SetEdit(false);
        _hook.SetLive(true);
        _paused = false;
        EnsureLoop();
    }

    private void StartEditor()
    {
        EnsureOverlay();
        _overlay!.ShowLive();
        _overlay.ShowHint();
        _hook.SetEdit(false);
        _hook.SetLive(true);
        _paused = false;
        EnsureLoop();
    }

    private void BeginPick(bool keepPrevious)
    {
        if (_picker != null) return;
        int gen = ++_generation;
        var cfg = _settings().Subtitle;
        int px = cfg.SourceX, py = cfg.SourceY, pw = cfg.SourceW, ph = cfg.SourceH;
        _paused = true;
        _hook.SetEdit(false);
        _hook.SetLive(false);
        _overlay?.HideHint();
        _overlay?.Hide();

        var picker = new SubtitleRegionPicker();
        _picker = picker;
        picker.Completed += (x, y, w, h) =>
        {
            if (gen != _generation) return;
            cfg.SetSource(x, y, w, h);
            if (!cfg.HasBox)
            {
                var box = SubtitleMath.DefaultBox(new Rectangle(x, y, w, h), ScreenCapture.VirtualScreenBounds);
                cfg.SetBox(box.X, box.Y, box.Width, box.Height);
            }
            _settings().Save();
            _picker = null;
            StartEditor();
        };
        picker.Cancelled += () =>
        {
            if (gen != _generation) return;
            _picker = null;
            if (keepPrevious && pw >= 8 && ph >= 8)
            {
                cfg.SetSource(px, py, pw, ph);
                StartEditor();
                return;
            }
            cfg.Enabled = false;
            _settings().Save();
            StopRuntime();
            _setChecked(false);
        };
        picker.Show();
        picker.Activate();
    }

    private void EnsureOverlay()
    {
        if (_overlay != null) return;
        _overlay = new SubtitleOverlayWindow(_settings().Subtitle);
        _overlay.Closed += (_, _) => _overlay = null;
    }

    private void EnsureLoop()
    {
        if (_loop != null && !_loop.IsCompleted) return;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => Loop(token), token);
    }

    private async Task Loop(CancellationToken ct)
    {
        using var grabber = new SubtitleGrabber();
        using var lens = new GoogleLensClient();
        var translator = new GoogleTextTranslateClient();
        var memory = new Dictionary<string, string>(StringComparer.Ordinal);
        var previous = new byte[SubtitleMath.SampleCount];
        var current = new byte[SubtitleMath.SampleCount];
        bool hasSample = false;
        string? shownSource = null;
        int emptyStreak = 0;
        var lastLens = DateTime.MinValue;
        Rectangle watched = Rectangle.Empty;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(600));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_paused) continue;
                var cfg = _settings();
                var sub = cfg.Subtitle;
                if (!sub.HasSource) continue;
                var region = new Rectangle(sub.SourceX, sub.SourceY, sub.SourceW, sub.SourceH);
                if (region != watched)
                {
                    watched = region;
                    hasSample = false;
                    shownSource = null;
                }

                var ui = System.Windows.Application.Current?.Dispatcher;
                ui?.Invoke(() =>
                {
                    _overlay?.SetHiddenFromCapture(true);
                    _overlay?.KeepOnTop();
                });
                byte[]? frame = grabber.Grab(region);
                ui?.Invoke(() => _overlay?.SetHiddenFromCapture(false));
                if (frame == null) continue;
                SubtitleMath.WriteSamples(frame, region.Width, region.Height, current);
                bool changed = !hasSample || SubtitleMath.ChangedSamples(previous, current) >= SubtitleMath.ChangeThreshold;
                (previous, current) = (current, previous);
                hasSample = true;
                if (!changed && shownSource != null)
                    continue;

                string? text = await SubtitleOcr.RecognizeAsync(frame, region.Width, region.Height, ct)
                    .ConfigureAwait(false);
                if (text == null && (shownSource == null || DateTime.UtcNow - lastLens >= TimeSpan.FromSeconds(2.5)))
                {
                    lastLens = DateTime.UtcNow;
                    text = await SubtitleOcr.RecognizeLensAsync(lens, frame, region.Width, region.Height, ct)
                        .ConfigureAwait(false);
                }

                if (text == null)
                {
                    if (shownSource == null && ++emptyStreak == 4)
                        _overlay?.SetLiveText("");
                    continue;
                }

                emptyStreak = 0;
                if (shownSource != null && SubtitleMath.NearlySame(text, shownSource))
                    continue;
                if (memory.TryGetValue(text, out var cached))
                {
                    shownSource = text;
                    _overlay?.SetLiveText(cached);
                    continue;
                }

                string lang = SubtitleMath.TargetLanguage(cfg.TranslateNativeLanguage);
                string shown = text;
                try
                {
                    var translated = await translator.TranslateAsync(text, lang, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(translated?.Text))
                        shown = translated.Value.Text.Trim();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    shown = text;
                }

                if (memory.Count > 64)
                {
                    string? drop = null;
                    foreach (var key in memory.Keys)
                    {
                        if (key != text) { drop = key; break; }
                    }
                    if (drop != null) memory.Remove(drop);
                }
                memory[text] = shown;
                shownSource = text;
                _overlay?.SetLiveText(shown);
            }
        }
        catch (OperationCanceledException)
        {
            /* durduruldu */
        }
    }

    private bool InsideBox(int x, int y)
    {
        if (_overlay is { IsVisible: true, IsEditing: true })
            return _overlay.Covers(x, y);
        var sub = _settings().Subtitle;
        return SubtitleMath.Contains(x, y, sub.BoxX, sub.BoxY, sub.BoxW, sub.BoxH);
    }

    private void OnDoubleClick()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_overlay == null || _paused) return;
            _overlay.EnterEdit();
            _hook.SetEdit(true);
        });
    }

    private void OnClickOutside()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_overlay is not { IsEditing: true }) return;
            _overlay.ExitEdit();
            _hook.SetEdit(false);
            _hook.SetLive(true);
        });
    }

    private void OnEscape() => OnClickOutside();

    private void StopRuntime()
    {
        _generation++;
        _paused = true;
        _hook.SetEdit(false);
        _hook.SetLive(false);
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        _picker?.Close();
        _picker = null;
        _overlay?.HideHint();
        _overlay?.Close();
        _overlay = null;
    }
}
