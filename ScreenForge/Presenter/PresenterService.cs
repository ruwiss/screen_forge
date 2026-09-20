using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SkiaSharp;
using ScreenForge.Settings;
using SfModifierKeys = ScreenForge.Settings.ModifierKeys;

namespace ScreenForge.Presenter;

public sealed class PresenterService : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly PresenterRenderer _renderer = new();
    private readonly InkBeautifier _beautify;
    private readonly PresenterMouseHook _mouse = new();
    private PresenterOverlayWindow? _overlay;
    private bool _disposed;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    private bool _exiting;
    private bool _spotlightOn;

    public PresenterService(Func<AppSettings> settings)
    {
        _settings = settings;
        _beautify = new InkBeautifier(_renderer);
        _beautify.Applied += () => _overlay?.Redraw();
        _mouse.Pressed += p => OnBegin(ToCanvas(p));
        _mouse.Moved += p => OnMove(ToCanvas(p));
        _mouse.Released += p => OnEnd(ToCanvas(p));
        _mouse.Escape += () =>
        {
            Cancel();
        };
        _mouse.TryEatKey = vk => TryEatBendKey(vk) || TryEatColorKey(vk) || TryEatUndoKey(vk);
        _mouse.AteKeyUp = OnAteKeyUp;
    }

    private HotkeyConfig BendHotkey =>
        Cfg.ArrowBendHotkey is { IsValid: true } hk
            ? hk
            : new HotkeyConfig { Key = "Space" };

    private bool TryEatBendKey(int vk)
    {
        if (_renderer.Tool != PresenterTool.Arrow && _renderer.DraftArrow == null)
            return false;
        if (!MatchesHotkey(BendHotkey, vk)) return false;
        SetBendHeld(true);
        return true;
    }

    private void OnAteKeyUp(int vk)
    {
        if (!MatchesHotkey(BendHotkey, vk)) return;
        SetBendHeld(false);
    }

    private void SetBendHeld(bool held)
    {
        _renderer.BendHeld = held;
        if (_overlay == null || _renderer.DraftArrow == null) return;
        _renderer.ApplyArrowBend(_overlay.CanvasBounds);
        _overlay.Redraw();
    }

    private bool TryEatColorKey(int vk)
    {
        if (_renderer.Tool is PresenterTool.None or PresenterTool.Laser)
            return false;
        if (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3)) return false;
        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)) return false;
        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5)) return false;
        if (IsDown(0x5B) || IsDown(0x5C)) return false;
        int slot = vk switch
        {
            0x31 or 0x61 => 0,
            0x32 or 0x62 => 1,
            0x33 or 0x63 => 2,
            0x34 or 0x64 => 3,
            0x35 or 0x65 => 4,
            _ => -1,
        };
        if (slot < 0) return false;
        ApplyInkSlot(slot);
        return true;
    }

    private void ApplyInkSlot(int slot)
    {
        if ((uint)slot >= (uint)Cfg.InkColors.Count) return;
        string hex = Cfg.InkColors[slot];
        var color = PresenterRenderer.Parse(hex, _renderer.StrokeColor);
        _renderer.SetInkColor(color);
        Cfg.PenColor = hex;
        _overlay?.Redraw();
    }

    private bool TryEatUndoKey(int vk)
    {
        if (vk != 0x5A) return false;
        if (!IsDown(0x11) && !IsDown(0xA2) && !IsDown(0xA3)) return false;
        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)) return false;
        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5)) return false;
        if (!_renderer.Undo())
            return false;
        _beautify.Reset();
        _overlay?.Redraw();
        return true;
    }

    private static bool MatchesHotkey(HotkeyConfig hk, int vk)
    {
        if (!hk.IsValid) return false;
        if (!Enum.TryParse<Key>(hk.Key, true, out var key) || key == Key.None)
            return false;
        if (KeyInterop.VirtualKeyFromKey(key) != vk)
            return false;
        var mods = SfModifierKeys.None;
        if (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3)) mods |= SfModifierKeys.Control;
        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)) mods |= SfModifierKeys.Shift;
        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5)) mods |= SfModifierKeys.Alt;
        if (IsDown(0x5B) || IsDown(0x5C)) mods |= SfModifierKeys.Windows;
        return hk.Modifiers == mods;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;

    private SKPoint ToCanvas(SKPoint screen) =>
        _overlay?.ToCanvas(screen) ?? screen;

    private PresenterSettings Cfg => _settings().Presenter;

    public void ToggleTool(PresenterTool tool)
    {
        if (_exiting) return;
        if (_renderer.Tool == tool)
        {
            ClearInk();
        }
        else
        {
            if (_renderer.IsDrawing)
                _renderer.End();
            _renderer.Tool = tool;
        }
        _renderer.StrokeColor = PresenterRenderer.Parse(Cfg.PenColor, _renderer.StrokeColor);
        _renderer.StrokeWidth = (float)Cfg.PenWidth;
        EnsureOverlay();
        UpdateInput();
    }

    public void ToggleSpotlight()
    {
        if (_exiting) return;
        _spotlightOn = !_spotlightOn;
        _renderer.Spotlight = _spotlightOn;
        EnsureOverlay();
        UpdateInput();
    }

    public void ApplyColor(string hex)
    {
        if (_exiting) return;
        var color = PresenterRenderer.Parse(hex, _renderer.StrokeColor);
        _renderer.StrokeColor = color;
        Cfg.PenColor = hex;
        if (_renderer.Tool is PresenterTool.None or PresenterTool.Laser)
            _renderer.Tool = PresenterTool.Pen;
        EnsureOverlay();
        UpdateInput();
    }

    public void BendArrow()
    {
        SetBendHeld(_renderer.BendHeld);
    }

    public void Cancel()
    {
        if (_exiting) return;
        _exiting = true;
        _renderer.Tool = PresenterTool.None;
        ClearInk();
        _spotlightOn = false;
        _renderer.Spotlight = false;
        UpdateInput();
        ShutdownOverlay();
    }

    private void EnsureOverlay()
    {
        if (_overlay != null) return;
        var prevMain = Application.Current?.MainWindow;
        _overlay = new PresenterOverlayWindow(Paint);
        _overlay.FrameTick += OnTick;
        _overlay.Closed += (_, _) =>
        {
            if (_overlay == null) return;
            _overlay = null;
            _mouse.Eat = false;
            _mouse.SessionActive = false;
            _exiting = false;
            _spotlightOn = false;
            _renderer.Tool = PresenterTool.None;
            _renderer.Spotlight = false;
            _renderer.SpotlightAmount = 0;
            ClearInk();
        };
        _overlay.Show();
        _mouse.SessionActive = true;
        if (prevMain != null && Application.Current != null)
            Application.Current.MainWindow = prevMain;
        UpdateInput();
    }

    private void Paint(SKCanvas canvas, int w, int h) => _renderer.Render(canvas, w, h, Cfg);

    private void OnBegin(SKPoint p)
    {
        _beautify.Pause();
        _renderer.Cursor = p;
        _renderer.Begin(p, Cfg);
        _overlay?.Redraw();
    }

    private void OnMove(SKPoint p)
    {
        _renderer.Cursor = p;
        if (_renderer.IsDrawing)
            _renderer.Move(p, Cfg);
        _overlay?.Redraw();
    }

    private void OnEnd(SKPoint p)
    {
        _renderer.Cursor = p;
        _renderer.End();
        if (_renderer.Tool == PresenterTool.Pen && _renderer.LastFreehand is { } stroke)
            _beautify.Enqueue(stroke);
        _overlay?.Redraw();
    }

    private void OnTick()
    {
        if (_overlay == null) return;
        var cursor = _overlay.CursorCanvas();
        _renderer.Cursor = cursor;

        float targetSpot = _spotlightOn ? 1f : 0f;
        float spotSpeed = 0.42f;
        _renderer.SpotlightAmount += (targetSpot - _renderer.SpotlightAmount) * spotSpeed;
        if (Math.Abs(_renderer.SpotlightAmount - targetSpot) < 0.01f)
            _renderer.SpotlightAmount = targetSpot;

        bool drawDirty = _renderer.Tick(Cfg);
        if (drawDirty || _renderer.Tool != PresenterTool.None || _spotlightOn)
            _overlay.Redraw();

        if (!_exiting && ShouldIdleClose())
            Application.Current?.Dispatcher.BeginInvoke(ShutdownOverlay, DispatcherPriority.Background);
    }

    private bool ShouldIdleClose()
    {
        if (_renderer.Tool != PresenterTool.None) return false;
        if (_spotlightOn || _renderer.SpotlightAmount > 0.02f) return false;
        if (_renderer.HasInk || _renderer.HasLaser) return false;
        return true;
    }

    private void UpdateInput()
    {
        _mouse.Eat = _renderer.Tool is not PresenterTool.None;
        _overlay?.Redraw();
    }

    private void ShutdownOverlay()
    {
        _mouse.Eat = false;
        _mouse.SessionActive = false;
        var overlay = _overlay;
        _overlay = null;
        overlay?.Close();
        _exiting = false;
        _spotlightOn = false;
        _renderer.Tool = PresenterTool.None;
        _renderer.Spotlight = false;
        _renderer.SpotlightAmount = 0;
        ClearInk();
    }

    private void ClearInk()
    {
        _beautify.Reset();
        _renderer.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShutdownOverlay();
        _mouse.Dispose();
    }
}
