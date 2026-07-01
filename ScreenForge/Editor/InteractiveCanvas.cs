using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using ScreenForge.Settings;

namespace ScreenForge.Editor;

/// <summary>
/// Sahneyi çizen ve fare etkileşimini yöneten tek tuval (immediate-mode).
/// Excalidraw/tldraw tarzı: öğe oluşturma, seçim, taşıma, 8-tutamaç boyutlandırma,
/// metin yerleştirme, blur/step ekleme — hepsi Skia ile çizilir.
/// </summary>
public sealed class InteractiveCanvas : SKElement
{
    public Scene Scene { get; }
    public ToolStyleMemory ToolStyle { get; }

    /// <summary>
    /// Yerleşim modu: Fit = tuvali pencereye ortala+ölçekle (kolaj/EditorWindow).
    /// OneToOne = bire bir, ölçeksiz (ekran-üstü in-place editör — canvas zaten sahne boyutunda yerleştirilir).
    /// </summary>
    public LayoutMode Layout { get; set; } = LayoutMode.Fit;

    private EditorTool _tool = EditorTool.Select;
    public EditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            // Araç değişince yarım kalan çizim/etkileşimi temizle (aksi halde fare yakalı kalıp
            // araç çubuğu tıklamalarını bloke edebilir).
            _interacting = false;
            _draftItem = null;
            _moving = false;
            _activeHandle = -1;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (_tool != EditorTool.Select) ClearSelection();
            InvalidateVisual();
            ToolChanged?.Invoke();
        }
    }

    /// <summary>Seçili öğeler (çoklu seçim). İlk öğe "birincil" kabul edilir.</summary>
    public List<SceneItem> Selection { get; } = new();
    public SceneItem? SelectedItem => Selection.Count > 0 ? Selection[0] : null;

    public event Action? ToolChanged;
    public event Action? SelectionChanged;
    public event Action? ItemMoved;   // sürükleme/resize sırasında her frame
    public event Action<TextItem>? TextEditRequested;
    public event Action<ImageItem>? CropRequested;

    // Crop modu (tekil resim kırpma)
    private ImageItem? _cropTarget;
    private SKRect _cropRect; // tuval (içerik) uzayında geçici crop dikdörtgeni
    public bool IsCropping => _cropTarget != null;

    // Sahne-düzeyi kırpma (serbest mod)
    private bool _sceneCropping;
    private SKRect _sceneCropRect;
    private bool _sceneCropDragging;
    private int _sceneCropHandle = -1;   // -1=yeni çiz, 0-7=handle resize, 8=içten taşı
    private SKPoint _sceneCropMoveOrigin;
    public bool IsSceneCropping => _sceneCropping;
    public event Action? SceneCropCommitted;

    // Etkileşim durumu
    private bool _interacting;
    private SKPoint _dragStart;
    private SceneItem? _draftItem;            // oluşturulmakta olan öğe
    private SceneItem? _beforeState;          // modify için snapshot
    private int _activeHandle = -1;           // boyutlandırma tutamacı
    private bool _moving;

    // Çoklu seçim
    private bool _marqueeActive;
    private SKPoint _marqueeCur;
    private bool _groupResize;                  // birleşik kutu boyutlandırma
    private SKRect _groupStartBounds;           // boyutlandırma başlangıç birleşik kutusu
    private List<SceneItem> _beforeStates = new(); // grup işlemi öncesi klonlar (undo)

    private const float HandleSize = 9f;

    // Cached paint objects — yeniden kullanılır, renk/genişlik değişince güncellenir.
    // SKPathEffect scale-bağımlı olduğundan SKPaint'leri scale değişince rebuild ederiz.
    private float _lastPaintScale = -1f;
    private SKPaint? _outlinePaint;
    private SKPaint? _handleFillPaint;
    private SKPaint? _handleStrokePaint;
    private SKPaint? _bendFillPaint;
    private SKPaint? _bendStrokePaint;
    private SKPaint? _marqueeFillPaint;
    private SKPaint? _marqueeStrokePaint;
    private SKPaint? _rotLinePaint;
    private SKPaint? _rotFillPaint;
    private SKPaint? _rotStrokePaint;
    private SKPaint? _crHaloPaint;
    private SKPaint? _crLinePaint;

    private void EnsureScalePaints(float scale)
    {
        if (Math.Abs(scale - _lastPaintScale) < 0.001f) return;
        _lastPaintScale = scale;

        _outlinePaint?.Dispose();
        _outlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2F, 0x6F, 0xED),
            StrokeWidth = 1.5f / scale,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f / scale, 4f / scale }, 0),
        };

        _handleFillPaint?.Dispose();
        _handleFillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = true };

        _handleStrokePaint?.Dispose();
        _handleStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2F, 0x6F, 0xED),
            StrokeWidth = 1.5f / scale,
            IsAntialias = true,
        };

        _bendFillPaint?.Dispose();
        _bendFillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xFF, 0xFF, 220), IsAntialias = true };

        _bendStrokePaint?.Dispose();
        _bendStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2F, 0x6F, 0xED),
            StrokeWidth = 1.5f / scale,
            IsAntialias = true,
        };

        _marqueeFillPaint?.Dispose();
        _marqueeFillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0x2F, 0x6F, 0xED, 40) };

        _marqueeStrokePaint?.Dispose();
        _marqueeStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2F, 0x6F, 0xED),
            StrokeWidth = 1f / scale,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f / scale, 3f / scale }, 0),
        };

        _rotLinePaint?.Dispose();
        _rotLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(0x2F, 0x6F, 0xED), StrokeWidth = 1f / scale, IsAntialias = true };

        _rotFillPaint?.Dispose();
        _rotFillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xFF, 0xFF, 220), IsAntialias = true };

        _rotStrokePaint?.Dispose();
        _rotStrokePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(0x2F, 0x6F, 0xED), StrokeWidth = 1.5f / scale, IsAntialias = true };

        _crHaloPaint?.Dispose();
        _crHaloPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0x2F, 0x6F, 0xED),
            StrokeWidth = 4.4f / scale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        _crLinePaint?.Dispose();
        _crLinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.White,
            StrokeWidth = 2.1f / scale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
    }

    // Frame limiter: dirty flag + CompositionTarget.Rendering → monitör refresh'e sync
    private bool _dirty;

    private new void InvalidateVisual()
    {
        _dirty = true;
    }

    private void OnRenderingTick(object? sender, EventArgs e)
    {
        if (_dirty) { _dirty = false; base.InvalidateVisual(); }
    }

    public InteractiveCanvas(Scene scene, ToolStyleMemory style)
    {
        Scene = scene;
        ToolStyle = style;
        Scene.Changed += OnSceneChanged;
        Scene.SelectionRestore += OnSelectionRestore;
        Focusable = true;
        ClipToBounds = true;
        System.Windows.Media.CompositionTarget.Rendering += OnRenderingTick;
    }

    // Pencere kapanınca event'i temizle (aksi halde her zaman ateşlenir)
    protected override void OnVisualParentChanged(System.Windows.DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (Parent == null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRenderingTick;
            Scene.Changed -= OnSceneChanged;
            Scene.SelectionRestore -= OnSelectionRestore;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScale = newDpi.DpiScaleX;
        _dpiScaleCached = true;
        _lastPaintScale = -1f; // scale bağımlı paint'leri yeniden oluştur
    }

    // ===================== Çizim =====================
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        if (e.Info.Width <= 0 || e.Info.Height <= 0) return;
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x16, 0x1A, 0x22));

        // Tuvali pencereye sığacak şekilde ölçekle + ortala
        var (scale, offset) = ComputeTransform(e.Info.Width, e.Info.Height);
        EnsureScalePaints(scale);
        canvas.Save();
        canvas.Translate(offset.X, offset.Y);
        canvas.Scale(scale);

        bool dragging = _interacting && (_moving || _activeHandle >= 0 || _marqueeActive || _draftItem != null);
        SceneRenderer.RenderContent(canvas, Scene, skipBlurSnapshot: dragging);

        // Taslak (oluşturulan) öğe
        _draftItem?.Render(canvas);

        if (IsCropping)
        {
            DrawCropOverlay(canvas, scale);
        }
        else if (_sceneCropping)
        {
            DrawSceneCropOverlay(canvas, scale);
        }
        else if (_tool == EditorTool.Select)
        {
            // Çoklu seçim: her öğenin ince çerçevesi + birleşik kutu tutamaçları
            if (Selection.Count > 1)
            {
                foreach (var it in Selection) DrawItemOutline(canvas, it.Bounds, scale);
                DrawHandles(canvas, SelectionBounds(), scale);
            }
            else if (SelectedItem != null)
            {
                DrawSelection(canvas, SelectedItem, scale);
            }

            // Marquee (sürükle-kutu)
            if (_marqueeActive)
                DrawMarquee(canvas, scale);
        }

        canvas.Restore();
    }

    private float _scale = 1f;
    private SKPoint _offset;

    private (float scale, SKPoint offset) ComputeTransform(int pxW, int pxH)
    {
        float cw = Scene.Width, ch = Scene.Height;
        if (cw <= 0 || ch <= 0) return (1f, new SKPoint(0, 0));

        // OneToOne: canvas, sahne ile aynı DIP boyutunda yerleştirildiğinden, cihaz pikseli ölçeği
        // = DPI ölçeği. Böylece bölge görüntüsü ekrandaki gerçek yerine bire bir oturur.
        if (Layout == LayoutMode.OneToOne)
        {
            float s = pxW > 0 && cw > 0 ? pxW / cw : 1f;
            if (s <= 0 || float.IsNaN(s)) s = (float)DpiScale;
            _scale = s; _offset = new SKPoint(0, 0);
            return (s, new SKPoint(0, 0));
        }

        float margin = 24f * (float)DpiScale;
        float availW = pxW - margin * 2, availH = pxH - margin * 2;
        float scale = Math.Min(availW / cw, availH / ch);
        scale = Math.Min(scale, 4f);
        if (scale <= 0 || float.IsNaN(scale)) scale = 1f;
        float ox = (pxW - cw * scale) / 2f;
        float oy = (pxH - ch * scale) / 2f;
        _scale = scale; _offset = new SKPoint(ox, oy);
        return (scale, new SKPoint(ox, oy));
    }

    private double _dpiScale = 1.0;
    private bool _dpiScaleCached;

    private double DpiScale
    {
        get
        {
            if (!_dpiScaleCached)
            {
                _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                _dpiScaleCached = true;
            }
            return _dpiScale;
        }
    }

    /// <summary>WPF fare noktası → sahne (içerik piksel) koordinatı.</summary>
    private SKPoint ToScene(Point wpf)
    {
        double dpi = DpiScale;
        float px = (float)(wpf.X * dpi), py = (float)(wpf.Y * dpi);
        return new SKPoint((px - _offset.X) / _scale, (py - _offset.Y) / _scale);
    }

    private void DrawSelection(SKCanvas canvas, SceneItem item, float scale)
    {
        canvas.Save();
        if (item.Rotation != 0)
        {
            var c = item.Center;
            canvas.RotateDegrees(item.Rotation, c.X, c.Y);
        }
        DrawItemOutline(canvas, item.Bounds, scale);
        DrawHandles(canvas, item.Bounds, scale, cornersOnly: item is TextItem);
        if (item is ArrowItem arrow)
            DrawBendHandle(canvas, arrow.BendHandlePos, scale);
        if (item is RectItem rectForCr)
            DrawCornerRadiusHandle(canvas, rectForCr.Bounds, rectForCr.CornerRadius, scale);
        else if (item is TextItem textForCr && textForCr.Ribbon)
            DrawCornerRadiusHandle(canvas, textForCr.Bounds, textForCr.RibbonRadius, scale);
        canvas.Restore();
        DrawRotationHandle(canvas, item, scale);
    }

    private static SKPoint RotationHandlePos(SceneItem item, float scale)
    {
        float dist = 22f / scale;
        var b = item.Bounds;
        var top = new SKPoint(b.MidX, b.Top - dist);
        if (item.Rotation == 0) return top;
        // Döndürülmüş tutamaç konumu: merkez etrafında döndür
        var c = item.Center;
        float rad = item.Rotation * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        float dx = top.X - c.X, dy = top.Y - c.Y;
        return new SKPoint(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
    }

    private void DrawRotationHandle(SKCanvas canvas, SceneItem item, float scale)
    {
        var pos = RotationHandlePos(item, scale);
        var top = new SKPoint(item.Bounds.MidX, item.Bounds.Top);
        if (item.Rotation != 0)
        {
            var c = item.Center;
            float rad = item.Rotation * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            float dx = top.X - c.X, dy = top.Y - c.Y;
            top = new SKPoint(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
        }
        float r = 5f / scale;
        canvas.DrawLine(top, pos, _rotLinePaint!);
        canvas.DrawCircle(pos, r, _rotFillPaint!);
        canvas.DrawCircle(pos, r, _rotStrokePaint!);
    }

    private void DrawCornerRadiusHandle(SKCanvas canvas, SKRect bounds, float cornerRadius, float scale)
    {
        float minDim = Math.Min(bounds.Width, bounds.Height);
        if (minDim < 20f) return;
        float s = Math.Clamp(minDim / 24f, 7f, 9f) / scale;
        var pos = CornerRadiusHandlePoint(bounds, scale);
        using var path = new SKPath();
        path.MoveTo(pos.X - s, pos.Y);
        path.CubicTo(pos.X - s * 0.35f, pos.Y, pos.X, pos.Y + s * 0.35f, pos.X, pos.Y + s);
        canvas.DrawPath(path, _crHaloPaint!);
        canvas.DrawPath(path, _crLinePaint!);
    }

    private static SKPoint CornerRadiusHandlePoint(SKRect bounds, float scale)
    {
        float gap = 11f / scale;
        return new SKPoint(bounds.Right + gap, bounds.Top - gap);
    }

    private void DrawBendHandle(SKCanvas canvas, SKPoint pos, float scale)
    {
        float r = 6f / scale;
        canvas.DrawCircle(pos, r, _bendFillPaint!);
        canvas.DrawCircle(pos, r, _bendStrokePaint!);
    }

    private void DrawItemOutline(SKCanvas canvas, SKRect b, float scale)
    {
        canvas.DrawRect(b, _outlinePaint!);
    }

    private void DrawHandles(SKCanvas canvas, SKRect b, float scale, bool cornersOnly = false)
    {
        float hs = HandleSize / scale;
        var pts = HandlePoints(b);
        for (int i = 0; i < pts.Length; i++)
        {
            if (cornersOnly && i % 2 != 0) continue;
            var h = pts[i];
            var r = new SKRect(h.X - hs / 2, h.Y - hs / 2, h.X + hs / 2, h.Y + hs / 2);
            canvas.DrawRect(r, _handleFillPaint!);
            canvas.DrawRect(r, _handleStrokePaint!);
        }
    }

    private void DrawMarquee(SKCanvas canvas, float scale)
    {
        var r = new SKRect(Math.Min(_dragStart.X, _marqueeCur.X), Math.Min(_dragStart.Y, _marqueeCur.Y),
                           Math.Max(_dragStart.X, _marqueeCur.X), Math.Max(_dragStart.Y, _marqueeCur.Y));
        canvas.DrawRect(r, _marqueeFillPaint!);
        canvas.DrawRect(r, _marqueeStrokePaint!);
    }

    private void DrawCropOverlay(SKCanvas canvas, float scale)
    {
        if (_cropTarget == null) return;
        // Karartma: resmin dışında kalan crop alanı
        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 140), Style = SKPaintStyle.Fill };
        canvas.Save();
        canvas.ClipRect(_cropRect, SKClipOperation.Difference);
        canvas.DrawRect(_cropTarget.Bounds, dim);
        canvas.Restore();

        using var border = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(0xFF, 0xD7, 0x00), StrokeWidth = 2f / scale, IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 8f / scale, 4f / scale }, 0) };
        canvas.DrawRect(_cropRect, border);

        float hs = HandleSize / scale;
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = true };
        var allPts = HandlePoints(_cropRect);
        foreach (int i in new[] { 1, 3, 5, 7 })
        {
            var h = allPts[i];
            var r = new SKRect(h.X - hs / 2, h.Y - hs / 2, h.X + hs / 2, h.Y + hs / 2);
            canvas.DrawRect(r, fill);
            canvas.DrawRect(r, border);
        }
    }

    private static SKPoint[] HandlePoints(SKRect b) => new[]
    {
        new SKPoint(b.Left, b.Top), new SKPoint(b.MidX, b.Top), new SKPoint(b.Right, b.Top),
        new SKPoint(b.Right, b.MidY), new SKPoint(b.Right, b.Bottom),
        new SKPoint(b.MidX, b.Bottom), new SKPoint(b.Left, b.Bottom), new SKPoint(b.Left, b.MidY),
    };

    private int HitHandle(SKPoint p)
    {
        if (SelectedItem == null) return -1;
        int h = HitHandleRect(p, SelectedItem.Bounds);
        if (h >= 0 && h % 2 != 0 && SelectedItem is TextItem) return -1;
        return h;
    }

    private int HitHandleRect(SKPoint p, SKRect b)
    {
        var pts = HandlePoints(b);
        float tol = (HandleSize + 4f) / _scale;
        for (int i = 0; i < pts.Length; i++)
            if (Math.Abs(p.X - pts[i].X) <= tol && Math.Abs(p.Y - pts[i].Y) <= tol) return i;
        return -1;
    }

    // ===================== Fare =====================
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = ToScene(e.GetPosition(this));

        // Çift tık: metin düzenle / resim crop
        if (e.ClickCount == 2)
        {
            var dhit = Scene.HitTest(p);
            if (dhit is TextItem dt) { SetSelection(dt); TextEditRequested?.Invoke(dt); return; }
            if (dhit is ImageItem dimg) { SetSelection(dimg); CropRequested?.Invoke(dimg); return; }
        }

        _dragStart = p;
        _interacting = true;
        CaptureMouse();

        // Sahne kırpma modu
        if (_sceneCropping)
        {
            _sceneCropDragging = true;
            bool hasRect = _sceneCropRect.Width >= 4 && _sceneCropRect.Height >= 4;
            if (hasRect)
            {
                int h = HitSceneCropHandle(p);
                if (h == 8)
                {
                    // İçten taşıma
                    _sceneCropHandle = 8;
                    _sceneCropMoveOrigin = p;
                }
                else if (h >= 0)
                {
                    // Handle resize
                    _sceneCropHandle = h;
                }
                else
                {
                    // Dışına tıklama → yeni rect çiz
                    _sceneCropHandle = -1;
                    _sceneCropRect = new SKRect(p.X, p.Y, p.X, p.Y);
                }
            }
            else
            {
                _sceneCropHandle = -1;
                _sceneCropRect = new SKRect(p.X, p.Y, p.X, p.Y);
            }
            InvalidateVisual();
            return;
        }

        // Crop modu: tutamaç sürükleme veya commit
        if (IsCropping)
        {
            _activeHandle = HitCropHandle(p);
            if (_activeHandle < 0) { CommitCrop(); _interacting = false; ReleaseMouseCapture(); }
            return;
        }

        if (_tool == EditorTool.Select)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // Çoklu seçim varsa birleşik kutu tutamacı?
            if (Selection.Count > 1)
            {
                int gh = HitHandleRect(p, SelectionBounds());
                if (gh >= 0)
                {
                    _groupResize = true;
                    _activeHandle = gh;
                    _groupStartBounds = SelectionBounds();
                    _beforeStates = Selection.Select(s => s.Clone()).ToList();
                    return;
                }
            }
            else if (SelectedItem != null)
            {
                // Ok bend tutamacı (handle 8)
                if (SelectedItem is ArrowItem arrowHit)
                {
                    float bendTol = 10f / _scale;
                    if (SKPoint.Distance(p, arrowHit.BendHandlePos) <= bendTol)
                    {
                        _activeHandle = 8;
                        _beforeState = SelectedItem.Clone();
                        return;
                    }
                }
                // Rotation tutamacı (handle 10)
                {
                    float rotTol = 10f / _scale;
                    var rotPos = RotationHandlePos(SelectedItem, _scale);
                    if (SKPoint.Distance(p, rotPos) <= rotTol)
                    { _activeHandle = 10; _beforeState = SelectedItem.Clone(); return; }
                }
                // Corner radius tutamacı (handle 9)
                {
                    float cr = -1f;
                    SKRect crBounds = default;
                    if (SelectedItem is RectItem rectHit) { cr = rectHit.CornerRadius; crBounds = rectHit.Bounds; }
                    else if (SelectedItem is TextItem textHit && textHit.Ribbon) { cr = textHit.RibbonRadius; crBounds = textHit.Bounds; }
                    if (cr >= 0)
                    {
                        float crTol = 11f / _scale;
                        var crPt = CornerRadiusHandlePoint(crBounds, _scale);
                        if (SKPoint.Distance(p, crPt) <= crTol)
                        { _activeHandle = 9; _beforeState = SelectedItem.Clone(); return; }
                    }
                }
                // Tek öğe tutamacı
                _activeHandle = HitHandle(p);
                if (_activeHandle >= 0)
                {
                    _beforeState = SelectedItem.Clone();
                    return;
                }
            }

            var hit = Scene.HitTest(p);

            if (ctrl && hit != null)
            {
                ToggleSelection(hit);
                _interacting = false; // ctrl-tık: sürükleme başlatma
                if (IsMouseCaptured) ReleaseMouseCapture();
                return;
            }

            if (hit != null)
            {
                if (!Selection.Contains(hit)) SetSelection(hit);
                _moving = true;
                _beforeStates = Selection.Select(s => s.Clone()).ToList();
                InvalidateVisual();
                return;
            }

            // Seçili öğenin bounds alanı içindeyse → bounds'tan sürükle
            if (SelectedItem != null && SelectedItem.Bounds.Contains(p.X, p.Y))
            {
                _moving = true;
                _beforeStates = Selection.Select(s => s.Clone()).ToList();
                InvalidateVisual();
                return;
            }

            // Boş alan → seçimi kaldır + marquee başlat (resize + panel gizlenir).
            ClearSelection();
            _marqueeActive = true;
            _marqueeCur = p;
            InvalidateVisual();
            return;
        }

        // Araç ile yeni öğe oluştur
        BeginDraft(p);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_interacting) { UpdateCursor(ToScene(e.GetPosition(this))); return; }
        var p = ToScene(e.GetPosition(this));

        if (_sceneCropping && _sceneCropDragging)
        {
            if (_sceneCropHandle == 8)
            {
                // Taşıma
                float dx = p.X - _sceneCropMoveOrigin.X, dy = p.Y - _sceneCropMoveOrigin.Y;
                _sceneCropMoveOrigin = p;
                var sw = Scene.Width; var sh = Scene.Height;
                float nl = Math.Clamp(_sceneCropRect.Left + dx, 0, sw - _sceneCropRect.Width);
                float nt = Math.Clamp(_sceneCropRect.Top + dy, 0, sh - _sceneCropRect.Height);
                _sceneCropRect = new SKRect(nl, nt, nl + _sceneCropRect.Width, nt + _sceneCropRect.Height);
            }
            else if (_sceneCropHandle >= 0)
            {
                // Handle resize
                ResizeSceneCropRect(p);
            }
            else
            {
                // Yeni çizim
                _sceneCropRect = new SKRect(
                    Math.Clamp(Math.Min(_dragStart.X, p.X), 0, Scene.Width),
                    Math.Clamp(Math.Min(_dragStart.Y, p.Y), 0, Scene.Height),
                    Math.Clamp(Math.Max(_dragStart.X, p.X), 0, Scene.Width),
                    Math.Clamp(Math.Max(_dragStart.Y, p.Y), 0, Scene.Height));
            }
            InvalidateVisual();
            return;
        }

        if (IsCropping)
        {
            if (_activeHandle >= 0) ResizeCropRect(p);
            InvalidateVisual();
            return;
        }

        if (_tool == EditorTool.Select)
        {
            if (_marqueeActive)
            {
                _marqueeCur = p;
            }
            else if (_groupResize)
            {
                ResizeGroup(p);
            }
            else if (_activeHandle == 8 && SelectedItem is ArrowItem arrowBend)
            {
                arrowBend.BendPoint = p;
                ItemMoved?.Invoke();
            }
            else if (_activeHandle == 10 && SelectedItem != null)
            {
                var c = SelectedItem.Center;
                float angle = (float)(Math.Atan2(p.Y - c.Y, p.X - c.X) * 180.0 / Math.PI) + 90f;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    angle = (float)Math.Round(angle / 15.0) * 15f; // Shift = 15° adımlar
                SelectedItem.Rotation = angle;
                ItemMoved?.Invoke();
            }
            else if (_activeHandle == 9 && SelectedItem is RectItem rectResize)
            {
                float maxR = Math.Min(rectResize.Bounds.Width, rectResize.Bounds.Height) / 2f;
                var corner = new SKPoint(rectResize.Bounds.Right, rectResize.Bounds.Top);
                float d = SKPoint.Distance(p, corner);
                rectResize.CornerRadius = Math.Clamp(d / 0.7071f, 0f, maxR);
                ItemMoved?.Invoke();
            }
            else if (_activeHandle == 9 && SelectedItem is TextItem textResize && textResize.Ribbon)
            {
                float maxR = Math.Min(textResize.Bounds.Width, textResize.Bounds.Height) / 2f;
                var corner = new SKPoint(textResize.Bounds.Right, textResize.Bounds.Top);
                float d = SKPoint.Distance(p, corner);
                textResize.RibbonRadius = Math.Clamp(d / 0.7071f, 0f, maxR);
                ItemMoved?.Invoke();
            }
            else if (_activeHandle >= 0 && Selection.Count == 1)
            {
                ResizeSelected(p);
                ItemMoved?.Invoke();
            }
            else if (_moving && Selection.Count > 0)
            {
                float dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
                foreach (var s in Selection) s.Move(dx, dy);
                _dragStart = p;
                ItemMoved?.Invoke();
            }
            InvalidateVisual();
            return;
        }

        UpdateDraft(p);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_interacting) return;
        _interacting = false;
        ReleaseMouseCapture();
        var p = ToScene(e.GetPosition(this));

        if (_sceneCropping && _sceneCropDragging)
        {
            _sceneCropDragging = false;
            _sceneCropHandle = -1;
            InvalidateVisual();
            return;
        }

        if (IsCropping) { _activeHandle = -1; return; }

        if (_tool == EditorTool.Select)
        {
            if (_marqueeActive)
            {
                _marqueeActive = false;
                var box = new SKRect(Math.Min(_dragStart.X, p.X), Math.Min(_dragStart.Y, p.Y),
                                     Math.Max(_dragStart.X, p.X), Math.Max(_dragStart.Y, p.Y));
                var inside = Scene.Items.Where(it => box.Contains(it.Bounds) || box.IntersectsWith(it.Bounds)).ToList();
                SetSelection(inside);
                InvalidateVisual();
                return;
            }

            if (_groupResize)
            {
                CommitGroupModify();
                _groupResize = false; _activeHandle = -1;
                return;
            }

            if (_activeHandle is 8 or 9 or 10 && _beforeState != null && SelectedItem != null)
            {
                Scene.Apply(new ModifyItemAction(SelectedItem, _beforeState, SelectedItem.Clone()));
            }
            else if (_activeHandle >= 0 && _activeHandle < 8 && Selection.Count == 1 && _beforeState != null)
            {
                Scene.Apply(new ModifyItemAction(SelectedItem!, _beforeState, SelectedItem!.Clone()));
            }
            else if (_moving && Selection.Count > 0 && _beforeStates.Count == Selection.Count)
            {
                CommitGroupModify();
            }
            _activeHandle = -1; _moving = false; _beforeState = null; _beforeStates = new();
            return;
        }

        CommitDraft(p);
    }

    // ===================== Sağ tık — Z-order menüsü =====================
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var p = ToScene(e.GetPosition(this));
        var hit = Scene.HitTest(p);
        if (hit == null) return;

        SetSelection(hit);
        InvalidateVisual();

        var menu = new ContextMenu();
        menu.Items.Add(MakeZMenuItem("En öne getir", hit, Scene.BringToFront));
        menu.Items.Add(MakeZMenuItem("Bir öne", hit, Scene.BringForward));
        menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSeparator") });
        menu.Items.Add(MakeZMenuItem("Bir arkaya", hit, Scene.SendBackward));
        menu.Items.Add(MakeZMenuItem("En arkaya at", hit, Scene.SendToBack));

        menu.Style = (Style)FindResource("DarkContextMenu");
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem MakeZMenuItem(string header, SceneItem item, Action<SceneItem> reorder)
    {
        var mi = new MenuItem { Header = header, Style = (Style)FindResource("DarkMenuItem") };
        mi.Click += (_, _) =>
        {
            var before = Scene.Items.ToList();
            reorder(item);
            var after = Scene.Items.ToList();
            if (!before.SequenceEqual(after))
            {
                Scene.Apply(new ReorderAction(before, after));
                InvalidateVisual();
            }
        };
        return mi;
    }

    // ===================== Taslak oluşturma =====================
    private void BeginDraft(SKPoint p)
    {
        switch (_tool)
        {
            case EditorTool.Rectangle:
                _draftItem = ApplyStroke(new RectItem { Bounds = new SKRect(p.X, p.Y, p.X, p.Y) });
                break;
            case EditorTool.Ellipse:
                _draftItem = ApplyStroke(new EllipseItem { Bounds = new SKRect(p.X, p.Y, p.X, p.Y) });
                break;
            case EditorTool.Line:
                _draftItem = ApplyStroke(new LineItem { Start = p, End = p });
                break;
            case EditorTool.Arrow:
                _draftItem = ApplyStroke(new ArrowItem { Start = p, End = p, HeadScale = (float)ToolStyle.ArrowHeadScale });
                break;
            case EditorTool.Pen:
                var fh = (FreehandItem)ApplyStroke(new FreehandItem());
                fh.AddPoint(p); _draftItem = fh;
                break;
            case EditorTool.Highlight:
                var hl = (HighlightItem)ApplyStroke(new HighlightItem());
                hl.StrokeColor = ColorFromHex(ToolStyle.StrokeColor);
                hl.AddPoint(p); _draftItem = hl;
                break;
            case EditorTool.Blur:
                _draftItem = new BlurItem { Bounds = new SKRect(p.X, p.Y, p.X, p.Y), Strength = (float)ToolStyle.BlurStrength, Pixelate = ToolStyle.BlurPixelate };
                break;
            case EditorTool.Text:
                CreateTextAt(p);
                _interacting = false;
                ReleaseMouseCapture();
                break;
            case EditorTool.Step:
                CreateStepAt(p);
                _interacting = false;
                ReleaseMouseCapture();
                break;
        }
    }

    private void UpdateDraft(SKPoint p)
    {
        switch (_draftItem)
        {
            case LineItem line:
                var ep = p;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    ep = SnapTo45(line.Start, p);
                line.End = ep; line.SyncBounds();
                break;
            case FreehandItem fh:
                fh.AddPoint(p);
                break;
            case BlurItem:
            case RectItem:
            case EllipseItem:
                float dLeft = Math.Min(_dragStart.X, p.X), dTop = Math.Min(_dragStart.Y, p.Y);
                float dRight = Math.Max(_dragStart.X, p.X), dBottom = Math.Max(_dragStart.Y, p.Y);
                if (_draftItem is RectItem or EllipseItem && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    float side = Math.Max(dRight - dLeft, dBottom - dTop);
                    dRight = dLeft + side; dBottom = dTop + side;
                }
                _draftItem.Bounds = new SKRect(dLeft, dTop, dRight, dBottom);
                break;
        }
    }

    private void CommitDraft(SKPoint p)
    {
        if (_draftItem == null) return;
        bool valid = _draftItem switch
        {
            LineItem l => SKPoint.Distance(l.Start, l.End) > 4,
            FreehandItem f => f.Points.Count > 1,
            _ => _draftItem.Bounds.Width > 4 && _draftItem.Bounds.Height > 4,
        };
        if (valid)
        {
            var item = _draftItem;
            Scene.Apply(new AddItemAction(item));
            // Dörtgen/Daire/Çizgi/Ok: Select'e geç + öğeyi seç (resize edilebilsin).
            // Diğerleri (Kalem/Highlight/Blur): araç aktif kalır → çoklu kullanım.
            if (_tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Line or EditorTool.Arrow)
            {
                Tool = EditorTool.Select;
                SetSelection(item);
            }
            else
            {
                ClearSelection();
            }
        }
        _draftItem = null;
        InvalidateVisual();
    }

    private void CreateTextAt(SKPoint p)
    {
        var t = new TextItem
        {
            Position = p,
            Text = "",
            FontFamily = ToolStyle.FontFamily,
            FontSize = (float)ToolStyle.FontSize,
            Bold = ToolStyle.FontBold,
            Shadow = ToolStyle.TextShadow,
            StrokeText = ToolStyle.TextStroke,
            StrokeTextColor = ColorFromHex(ToolStyle.TextStrokeColor),
            Ribbon = ToolStyle.TextRibbon,
            RibbonColor = ColorFromHex(ToolStyle.TextRibbonColor),
            StrokeColor = ColorFromHex(ToolStyle.TextColor),
        };
        Scene.Apply(new AddItemAction(t));
        Tool = EditorTool.Select;
        SetSelection(t);
        TextEditRequested?.Invoke(t);
    }

    private void CreateStepAt(SKPoint p)
    {
        var s = new StepItem
        {
            Position = p,
            Number = Scene.NextStepNumber,
            Shape = ToolStyle.StepShape,
            BadgeColor = ColorFromHex(ToolStyle.StepColor),
            NumberColor = ColorFromHex(ToolStyle.StepTextColor),
            Diameter = (float)ToolStyle.StepSize,
        };
        s.SyncBounds();
        Scene.Apply(new AddItemAction(s));
        // Step araçı aktif kalır (çoklu kullanım); seçim yok.
        ClearSelection();
        InvalidateVisual();
    }

    private SceneItem ApplyStroke(SceneItem item)
    {
        item.StrokeColor = ColorFromHex(ToolStyle.StrokeColor);
        item.FillColor = ColorFromHex(ToolStyle.FillColor);
        item.StrokeWidth = (float)ToolStyle.StrokeWidth;
        item.Opacity = (float)ToolStyle.Opacity;
        return item;
    }

    // ===================== Boyutlandırma =====================
    private void ResizeSelected(SKPoint p)
    {
        var item = SelectedItem!;
        var b = _beforeState?.Bounds ?? item.Bounds;
        float left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;

        bool isCorner = _activeHandle is 0 or 2 or 4 or 6;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool isImage = item is ImageItem;
        bool lockAspect = isCorner && (isImage ? !shift : shift) && b.Width > 0 && b.Height > 0;

        switch (_activeHandle)
        {
            case 0: left = p.X; top = p.Y; break;
            case 1: top = p.Y; break;
            case 2: right = p.X; top = p.Y; break;
            case 3: right = p.X; break;
            case 4: right = p.X; bottom = p.Y; break;
            case 5: bottom = p.Y; break;
            case 6: left = p.X; bottom = p.Y; break;
            case 7: left = p.X; break;
        }

        if (lockAspect)
        {
            float aspect = b.Width / b.Height;
            float newW = right - left, newH = bottom - top;
            // Genişlik baskın → yüksekliği ona göre; karşı köşe sabit.
            if (Math.Abs(newW) / aspect >= Math.Abs(newH))
                newH = Math.Abs(newW) / aspect * Math.Sign(newH == 0 ? 1 : newH);
            else
                newW = Math.Abs(newH) * aspect * Math.Sign(newW == 0 ? 1 : newW);
            switch (_activeHandle)
            {
                case 0: left = right - newW; top = bottom - newH; break;   // sabit: sağ-alt
                case 2: right = left + newW; top = bottom - newH; break;   // sabit: sol-alt
                case 4: right = left + newW; bottom = top + newH; break;   // sabit: sol-üst
                case 6: left = right - newW; bottom = top + newH; break;   // sabit: sağ-üst
            }
        }

        var nb = new SKRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
        if (nb.Width < item.MinSize || nb.Height < item.MinSize) return;

        if (item is TextItem t)
        {
            var bt = _beforeState as TextItem;
            float origFontSize = bt?.FontSize ?? t.FontSize;
            float ratio = b.Height > 0 ? nb.Height / b.Height : 1f;
            t.FontSize = Math.Max(6f, origFontSize * ratio);

            // Anchor: sürüklenen handle'ın karşı kenarı sabit kalır
            // Handle 0=sol-üst, 1=üst, 2=sağ-üst, 3=sağ, 4=sağ-alt, 5=alt, 6=sol-alt, 7=sol
            bool anchorRight = _activeHandle is 0 or 6 or 7;
            bool anchorBottom = _activeHandle is 0 or 1 or 2;

            // Anchor koordinatları _beforeState'ten (sabit referans)
            float anchorX = anchorRight ? b.Right : b.Left;
            float anchorY = anchorBottom ? b.Bottom : b.Top;

            // Geçici ölç → gerçek metin boyutunu al
            t.Position = SKPoint.Empty;
            t.Measure();
            float realW = t.Bounds.Width;
            float realH = t.Bounds.Height;

            float padH = t.Ribbon ? t.RibbonPadding : 0;
            float padV = t.Ribbon ? t.RibbonPaddingV : 0;

            float posX = anchorRight ? anchorX - realW + padH : anchorX + padH;
            float posY = anchorBottom ? anchorY - realH + padV : anchorY + padV;

            t.Position = new SKPoint(posX, posY);
            t.Measure();
            return;
        }

        ApplyResize(item, b, nb, _beforeState);
    }

    /// <summary>Çoklu seçimi birleşik kutu üzerinden orantılı yeniden boyutlandırır (oran korunur).</summary>
    private void ResizeGroup(SKPoint p)
    {
        var ob = _groupStartBounds;
        if (ob.Width <= 0 || ob.Height <= 0) return;
        float left = ob.Left, top = ob.Top, right = ob.Right, bottom = ob.Bottom;
        switch (_activeHandle)
        {
            case 0: left = p.X; top = p.Y; break;
            case 1: top = p.Y; break;
            case 2: right = p.X; top = p.Y; break;
            case 3: right = p.X; break;
            case 4: right = p.X; bottom = p.Y; break;
            case 5: bottom = p.Y; break;
            case 6: left = p.X; bottom = p.Y; break;
            case 7: left = p.X; break;
        }
        var nb = new SKRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
        if (nb.Width < 8 || nb.Height < 8) return;

        // Her öğeyi, klonundan (başlangıç hali) birleşik kutu oranına göre yeniden eşle.
        for (int i = 0; i < Selection.Count && i < _beforeStates.Count; i++)
        {
            var item = Selection[i];
            var orig = _beforeStates[i];
            item.RestoreFrom(orig);                      // başlangıç haline dön
            ApplyResize(item, ob, MapRect(orig.Bounds, ob, nb));
        }
    }

    /// <summary>Bir alt-dikdörtgeni eski birleşik kutudan yeni birleşik kutuya orantılı taşır.</summary>
    private static SKRect MapRect(SKRect inner, SKRect from, SKRect to)
    {
        var tl = MapPoint(new SKPoint(inner.Left, inner.Top), from, to);
        var br = MapPoint(new SKPoint(inner.Right, inner.Bottom), from, to);
        return new SKRect(tl.X, tl.Y, br.X, br.Y);
    }

    /// <summary>Grup taşıma/boyutlandırma sonrası tek geri-al adımı üretir.</summary>
    private void CommitGroupModify()
    {
        if (_beforeStates.Count != Selection.Count || Selection.Count == 0) return;
        var actions = new List<IUndoableAction>();
        for (int i = 0; i < Selection.Count; i++)
            actions.Add(new ModifyItemAction(Selection[i], _beforeStates[i], Selection[i].Clone()));
        Scene.Apply(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
        _beforeStates = new();
    }

    private static void ApplyResize(SceneItem item, SKRect oldB, SKRect newB, SceneItem? beforeState = null)
    {
        switch (item)
        {
            case ArrowItem arrow:
                var beforeArrow = beforeState as ArrowItem;
                var startSrc = beforeArrow != null ? beforeArrow.Start : arrow.Start;
                var endSrc = beforeArrow != null ? beforeArrow.End : arrow.End;
                arrow.Start = MapPoint(startSrc, oldB, newB);
                arrow.End = MapPoint(endSrc, oldB, newB);
                if ((beforeArrow?.BendPoint ?? arrow.BendPoint) is { } bp)
                    arrow.BendPoint = MapPoint(bp, oldB, newB);
                arrow.SyncBounds();
                break;
            case LineItem line:
                var beforeLine = beforeState as LineItem;
                line.Start = MapPoint(beforeLine != null ? beforeLine.Start : line.Start, oldB, newB);
                line.End = MapPoint(beforeLine != null ? beforeLine.End : line.End, oldB, newB);
                line.SyncBounds();
                break;
            case FreehandItem fh:
                for (int i = 0; i < fh.Points.Count; i++)
                    fh.Points[i] = MapPoint(fh.Points[i], oldB, newB);
                fh.Bounds = newB;
                break;
            case StepItem s:
                float newDiam = Math.Max(Math.Min(newB.Width, newB.Height), 12f);
                s.Diameter = newDiam;
                s.Position = new SKPoint(oldB.MidX, oldB.MidY);
                s.SyncBounds();
                break;
            case TextItem t:
                var bt2 = beforeState as TextItem;
                float origFontSize = bt2?.FontSize ?? t.FontSize;
                float ratioH = oldB.Height > 0 ? newB.Height / oldB.Height : 1f;
                t.FontSize = Math.Max(6f, origFontSize * ratioH);
                float padH2 = t.Ribbon ? t.RibbonPadding : 0;
                float padV2 = t.Ribbon ? t.RibbonPaddingV : 0;
                t.Position = new SKPoint(newB.Left + padH2, newB.Top + padV2);
                t.Measure();
                break;
            default:
                item.Bounds = newB;
                break;
        }
    }

    private static SKPoint MapPoint(SKPoint p, SKRect from, SKRect to)
    {
        float fx = from.Width > 0 ? (p.X - from.Left) / from.Width : 0;
        float fy = from.Height > 0 ? (p.Y - from.Top) / from.Height : 0;
        return new SKPoint(to.Left + fx * to.Width, to.Top + fy * to.Height);
    }

    // ===================== Seçim / silme =====================
    // Sahne değişince (undo/redo dahil): artık var olmayan öğeleri seçimden düşür,
    // aksi halde silinen objenin resize tutamaçları ekranda kalır.
    private void OnSceneChanged()
    {
        PruneSelection();
        InvalidateVisual();
    }

    // Undo/Redo sonrası ilgili öğeyi tekrar seç (ör. redo edilen resize işlemi).
    private void OnSelectionRestore(IReadOnlyList<SceneItem> items)
    {
        var valid = items.Where(i => Scene.Items.Contains(i)).ToList();
        if (valid.Count == 0) { ClearSelection(); return; }
        _activeHandle = -1;
        _groupResize = false;
        SetSelection(valid);
    }

    private void PruneSelection()
    {
        int removed = Selection.RemoveAll(s => !Scene.Items.Contains(s));
        if (removed > 0)
        {
            _activeHandle = -1;
            _groupResize = false;
            SelectionChanged?.Invoke();
        }
    }

    public void SetSelection(SceneItem? item)
    {
        Selection.Clear();
        if (item != null) Selection.Add(item);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetSelection(IEnumerable<SceneItem> items)
    {
        Selection.Clear();
        Selection.AddRange(items);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Ctrl+tık: öğeyi seçime ekle/çıkar.</summary>
    public void ToggleSelection(SceneItem item)
    {
        if (!Selection.Remove(item)) Selection.Add(item);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Selection.Count == 0) return;
        var actions = Selection.Select(s => (IUndoableAction)new RemoveItemAction(s)).ToList();
        Scene.Apply(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
        SetSelection((SceneItem?)null);
    }

    /// <summary>Seçili öğeleri klavye yön tuşlarıyla kaydırır (undo destekli).</summary>
    public void NudgeSelection(float dx, float dy)
    {
        if (Selection.Count == 0) return;
        var befores = Selection.Select(s => s.Clone()).ToList();
        foreach (var s in Selection) s.Move(dx, dy);
        var actions = new List<IUndoableAction>();
        for (int i = 0; i < Selection.Count; i++)
            actions.Add(new ModifyItemAction(Selection[i], befores[i], Selection[i].Clone()));
        Scene.Apply(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
        ItemMoved?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Seçili öğelerin birleşik (union) sınırları.</summary>
    public SKRect SelectionBounds()
    {
        if (Selection.Count == 0) return SKRect.Empty;
        var r = Selection[0].Bounds;
        foreach (var s in Selection.Skip(1)) r = SKRect.Union(r, s.Bounds);
        return r;
    }

    // ===================== Sahne kırpma (serbest mod) =====================
    public void BeginSceneCrop()
    {
        _sceneCropping = true;
        _sceneCropDragging = false;
        _sceneCropHandle = -1;
        _sceneCropRect = SKRect.Empty;   // kullanıcı sürükleyince oluşur
        ClearSelection();
        InvalidateVisual();
    }

    public void CommitSceneCrop()
    {
        if (!_sceneCropping) return;
        var cr = _sceneCropRect;
        if (cr.Width >= 4 && cr.Height >= 4)
            Scene.Apply(new SceneCropAction(Scene, cr));
        _sceneCropping = false;
        _sceneCropDragging = false;
        InvalidateVisual();
        SceneCropCommitted?.Invoke();
    }

    public void CancelSceneCrop()
    {
        _sceneCropping = false;
        _sceneCropDragging = false;
        _sceneCropHandle = -1;
        InvalidateVisual();
    }

    // h=0..7 handle index (HandlePoints sırasıyla), h=8 içten taşıma, -1 dışarı
    private int HitSceneCropHandle(SKPoint p)
    {
        if (_sceneCropRect.Width < 4 || _sceneCropRect.Height < 4) return -1;
        float tol = (HandleSize + 6f) / _scale;
        var pts = HandlePoints(_sceneCropRect);
        for (int i = 0; i < pts.Length; i++)
            if (Math.Abs(p.X - pts[i].X) <= tol && Math.Abs(p.Y - pts[i].Y) <= tol) return i;
        if (_sceneCropRect.Contains(p)) return 8;
        return -1;
    }

    private void ResizeSceneCropRect(SKPoint p)
    {
        float sw = Scene.Width, sh = Scene.Height;
        float l = _sceneCropRect.Left, t = _sceneCropRect.Top, r = _sceneCropRect.Right, b = _sceneCropRect.Bottom;
        float minSize = 8f;
        // HandlePoints: 0=TL,1=TC,2=TR,3=CR,4=BR,5=BC,6=BL,7=CL
        switch (_sceneCropHandle)
        {
            case 0: l = Math.Clamp(p.X, 0, r - minSize); t = Math.Clamp(p.Y, 0, b - minSize); break;
            case 1: t = Math.Clamp(p.Y, 0, b - minSize); break;
            case 2: r = Math.Clamp(p.X, l + minSize, sw); t = Math.Clamp(p.Y, 0, b - minSize); break;
            case 3: r = Math.Clamp(p.X, l + minSize, sw); break;
            case 4: r = Math.Clamp(p.X, l + minSize, sw); b = Math.Clamp(p.Y, t + minSize, sh); break;
            case 5: b = Math.Clamp(p.Y, t + minSize, sh); break;
            case 6: l = Math.Clamp(p.X, 0, r - minSize); b = Math.Clamp(p.Y, t + minSize, sh); break;
            case 7: l = Math.Clamp(p.X, 0, r - minSize); break;
        }
        _sceneCropRect = new SKRect(l, t, r, b);
    }

    private void DrawSceneCropOverlay(SKCanvas canvas, float scale)
    {
        bool hasRect = _sceneCropRect.Width >= 4 && _sceneCropRect.Height >= 4;
        var sceneBounds = new SKRect(0, 0, Scene.Width, Scene.Height);
        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 140), Style = SKPaintStyle.Fill };

        if (hasRect)
        {
            canvas.Save();
            canvas.ClipRect(_sceneCropRect, SKClipOperation.Difference);
            canvas.DrawRect(sceneBounds, dim);
            canvas.Restore();
        }
        else
        {
            canvas.DrawRect(sceneBounds, dim);
        }

        if (!hasRect) return;

        using var border = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0xFF, 0xD7, 0x00),
            StrokeWidth = 1.5f / scale,
            IsAntialias = true,
        };
        canvas.DrawRect(_sceneCropRect, border);

        // Üçte-bir kılavuz çizgileri
        float gx1 = _sceneCropRect.Left + _sceneCropRect.Width / 3f;
        float gx2 = _sceneCropRect.Left + _sceneCropRect.Width * 2f / 3f;
        float gy1 = _sceneCropRect.Top + _sceneCropRect.Height / 3f;
        float gy2 = _sceneCropRect.Top + _sceneCropRect.Height * 2f / 3f;
        using var guide = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x00, 60), StrokeWidth = 0.7f / scale, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(gx1, _sceneCropRect.Top, gx1, _sceneCropRect.Bottom, guide);
        canvas.DrawLine(gx2, _sceneCropRect.Top, gx2, _sceneCropRect.Bottom, guide);
        canvas.DrawLine(_sceneCropRect.Left, gy1, _sceneCropRect.Right, gy1, guide);
        canvas.DrawLine(_sceneCropRect.Left, gy2, _sceneCropRect.Right, gy2, guide);

        // 8 handle
        float hs = HandleSize / scale;
        using var hFill = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = true };
        using var hBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(0xFF, 0xD7, 0x00), StrokeWidth = 1.5f / scale, IsAntialias = true };
        var allPts = HandlePoints(_sceneCropRect);
        for (int i = 0; i < allPts.Length; i++)
        {
            var h = allPts[i];
            var hr = new SKRect(h.X - hs / 2, h.Y - hs / 2, h.X + hs / 2, h.Y + hs / 2);
            canvas.DrawRect(hr, hFill);
            canvas.DrawRect(hr, hBorder);
        }
    }

    // ===================== Crop modu =====================
    /// <summary>Bir resmi kırpma moduna alır (kenarlardan sürüklenebilir).</summary>
    public void BeginCrop(ImageItem img)
    {
        _cropTarget = img;
        _cropRect = img.Bounds;
        Tool = EditorTool.Select;
        SetSelection(img);
        InvalidateVisual();
    }

    /// <summary>Crop'u uygular: yeni Bounds + kaynak CropRect hesaplanır.</summary>
    public void CommitCrop()
    {
        if (_cropTarget == null) return;
        var img = _cropTarget;
        var before = img.Clone();

        // Geçerli görünen bölge (eski Bounds ∩ cropRect)
        var visible = SKRect.Intersect(img.Bounds, _cropRect);
        if (visible.Width >= img.MinSize && visible.Height >= img.MinSize)
        {
            // Görünür alanın resmin kaynağındaki karşılığını hesapla.
            var srcFull = img.CropRect ?? new SKRect(0, 0, img.Bitmap.Width, img.Bitmap.Height);
            float fx = (visible.Left - img.Bounds.Left) / img.Bounds.Width;
            float fy = (visible.Top - img.Bounds.Top) / img.Bounds.Height;
            float fw = visible.Width / img.Bounds.Width;
            float fh = visible.Height / img.Bounds.Height;
            var newSrc = new SKRect(
                srcFull.Left + fx * srcFull.Width,
                srcFull.Top + fy * srcFull.Height,
                srcFull.Left + (fx + fw) * srcFull.Width,
                srcFull.Top + (fy + fh) * srcFull.Height);
            img.CropRect = newSrc;
            img.Bounds = visible;

            var after = img.Clone();
            Scene.Apply(new ModifyItemAction(img, before, after));
        }
        _cropTarget = null;
        InvalidateVisual();
    }

    public void CancelCrop()
    {
        _cropTarget = null;
        InvalidateVisual();
    }

    private int HitCropHandle(SKPoint p)
    {
        var pts = HandlePoints(_cropRect);
        float tol = (HandleSize + 6f) / _scale;
        foreach (int i in new[] { 1, 3, 5, 7 })
            if (Math.Abs(p.X - pts[i].X) <= tol && Math.Abs(p.Y - pts[i].Y) <= tol) return i;
        return -1;
    }

    private void ResizeCropRect(SKPoint p)
    {
        if (_cropTarget == null) return;
        var b = _cropTarget.Bounds;
        float left = _cropRect.Left, top = _cropRect.Top, right = _cropRect.Right, bottom = _cropRect.Bottom;
        float px = Math.Clamp(p.X, b.Left, b.Right), py = Math.Clamp(p.Y, b.Top, b.Bottom);
        bool symmetric = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        switch (_activeHandle)
        {
            case 1: top = py; if (symmetric) { bottom = b.Bottom - (py - b.Top); } break;
            case 3: right = px; if (symmetric) { left = b.Left - (px - b.Right); } break;
            case 5: bottom = py; if (symmetric) { top = b.Top - (py - b.Bottom); } break;
            case 7: left = px; if (symmetric) { right = b.Right - (px - b.Left); } break;
        }
        left = Math.Clamp(left, b.Left, b.Right);
        top = Math.Clamp(top, b.Top, b.Bottom);
        right = Math.Clamp(right, b.Left, b.Right);
        bottom = Math.Clamp(bottom, b.Top, b.Bottom);
        _cropRect = new SKRect(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
    }

    private System.Windows.Input.Cursor? _lastCursor;

    private void SetCursor(System.Windows.Input.Cursor c)
    {
        if (!ReferenceEquals(c, _lastCursor)) { Cursor = c; _lastCursor = c; }
    }

    private void UpdateCursor(SKPoint p)
    {
        if (_sceneCropping)
        {
            if (_sceneCropRect.Width >= 4 && _sceneCropRect.Height >= 4)
            {
                int h = HitSceneCropHandle(p);
                SetCursor(h switch
                {
                    0 or 4 => Cursors.SizeNWSE,
                    2 or 6 => Cursors.SizeNESW,
                    1 or 5 => Cursors.SizeNS,
                    3 or 7 => Cursors.SizeWE,
                    8 => Cursors.SizeAll,
                    _ => Cursors.Cross,
                });
            }
            else
            {
                SetCursor(Cursors.Cross);
            }
            return;
        }

        if (_tool != EditorTool.Select) { SetCursor(Cursors.Cross); return; }

        if (SelectedItem != null)
        {
            // Seçili öğe bounds'unun büyük ölçekte dışındaysa pahalı handle testlerini atla
            var inflated = SelectedItem.Bounds;
            float quickTol = 30f / _scale;
            bool nearItem = p.X >= inflated.Left - quickTol && p.X <= inflated.Right + quickTol
                         && p.Y >= inflated.Top - quickTol && p.Y <= inflated.Bottom + quickTol;

            if (nearItem || SelectedItem.Rotation != 0)
            {
                // Rotation tutamacı (handle 10)
                float rotTol = 10f / _scale;
                if (SKPoint.Distance(p, RotationHandlePos(SelectedItem, _scale)) <= rotTol)
                { SetCursor(Cursors.Hand); return; }

                // Corner radius tutamacı (handle 9)
                float crVal = -1f; SKRect crB = default;
                if (SelectedItem is RectItem rc) { crVal = rc.CornerRadius; crB = rc.Bounds; }
                else if (SelectedItem is TextItem tc && tc.Ribbon) { crVal = tc.RibbonRadius; crB = tc.Bounds; }
                if (crVal >= 0)
                {
                    float crTol = 11f / _scale;
                    var crPt = CornerRadiusHandlePoint(crB, _scale);
                    if (SKPoint.Distance(p, crPt) <= crTol) { SetCursor(Cursors.SizeAll); return; }
                }

                int h = HitHandle(p);
                if (h >= 0) { SetCursor(h is 0 or 4 ? Cursors.SizeNWSE : h is 2 or 6 ? Cursors.SizeNESW : h is 1 or 5 ? Cursors.SizeNS : Cursors.SizeWE); return; }
                if (SelectedItem.Bounds.Contains(p.X, p.Y)) { SetCursor(Cursors.SizeAll); return; }
            }
        }

        SetCursor(Scene.HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow);
    }

    // ===================== Yardımcılar =====================
    private static SKPoint SnapTo45(SKPoint origin, SKPoint p)
    {
        float dx = p.X - origin.X, dy = p.Y - origin.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return origin;
        double ang = Math.Atan2(dy, dx);
        double snapped = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
        return new SKPoint(origin.X + len * (float)Math.Cos(snapped), origin.Y + len * (float)Math.Sin(snapped));
    }

    public static SKColor ColorFromHex(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return new SKColor(r, g, b, a);
            }
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new SKColor(r, g, b);
            }
        }
        catch { }
        return SKColors.Transparent;
    }

    public static string HexFromColor(SKColor c) => $"#{c.Alpha:X2}{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
}
