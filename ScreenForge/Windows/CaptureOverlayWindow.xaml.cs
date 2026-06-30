using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp;
using ScreenForge.Capture;
using ScreenForge.Editor;
using ScreenForge.Settings;
using ScreenForge.Upload;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace ScreenForge.Windows;

/// <summary>Yakalama modu (overlay açılışında seçilir; seçim öncesi değiştirilebilir).</summary>
public enum CaptureMode { Region, FullScreen, Free }

/// <summary>
/// Lightshot tarzı birleşik ekran-üstü yakalama + düzenleme katmanı. Tek pencere, üç mod
/// (Bölgesel / Tam ekran / Serbest), ayrı editör penceresi yok.
/// </summary>
public partial class CaptureOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    // Pencere algılama (bölge seçimi öncesi hover)
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    // DWM extended frame bounds — gölge hariç görünür çerçeve (Windows 10+)
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out WinRect pvAttribute, int cbAttribute);
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    private enum Phase { Select, Edit }

    private readonly Bitmap _screenshot;
    private readonly Rectangle _virtualBounds;
    private readonly AppSettings _settings;

    private CaptureMode _mode;
    private Phase _phase = Phase.Select;
    private bool _dragging;
    private bool _pendingNewSelection;
    private bool _newSelectionArmed;
    private WpfPoint _start;

    private WpfRect _selDip;
    private Rectangle _pixelRegion;
    private Scene? _scene;
    private InteractiveCanvas? _canvas;
    private bool _textEditing;   // inline metin düzenleme açıkken araç kısayollarını engelle

    // Pencere algılama — sadece ilk Region+Select fazında aktif
    private bool _windowHoverActive = true;
    private List<(IntPtr hwnd, WpfRect dip)> _visibleWindows = new();
    private WpfRect _hoveredWindowDip = WpfRect.Empty;
    // Pencere tıklandı ama henüz commit edilmedi (sürüklemeye başlarsa iptal)
    private bool _windowClickPending;
    private WpfRect _windowClickBounds;
    private bool _selResizing;
    private int _selResizeEdge = -1; // 0=top,1=right,2=bottom,3=left,4=TL,5=TR,6=BR,7=BL

    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = new();
    private bool _toolbarDragging;
    private WpfPoint _toolbarDragStart;
    private bool _toolbarMoved;        // kullanıcı araç çubuğunu sürükledi mi (tam ekran/serbest)
    private WpfPoint _toolbarPos;      // kullanıcının seçtiği konum

    public CaptureOverlayWindow(Bitmap screenshot, Rectangle virtualBounds, AppSettings settings, CaptureMode mode = CaptureMode.Region)
    {
        InitializeComponent();
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _settings = settings;
        _mode = mode;
        ScreenImage.Source = ScreenCapture.ToBitmapSource(screenshot);

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;

        if (_mode == CaptureMode.Region)
            ToolbarGrip.Visibility = System.Windows.Visibility.Collapsed;
        ToolbarGrip.MouseLeftButtonDown += OnToolbarGripDown;
        ToolbarGrip.MouseMove += OnToolbarGripMove;
        ToolbarGrip.MouseLeftButtonUp += OnToolbarGripUp;
    }

    private void OnToolbarGripDown(object sender, MouseButtonEventArgs e)
    {
        _toolbarDragging = true;
        _toolbarDragStart = e.GetPosition(Root);
        ToolbarGrip.CaptureMouse();
        e.Handled = true;
    }

    private void OnToolbarGripMove(object sender, MouseEventArgs e)
    {
        if (!_toolbarDragging) return;
        var pos = e.GetPosition(Root);
        double dx = pos.X - _toolbarDragStart.X;
        double dy = pos.Y - _toolbarDragStart.Y;
        double nx = Canvas.GetLeft(Toolbar) + dx;
        double ny = Canvas.GetTop(Toolbar) + dy;
        ClampToolbar(ref nx, ref ny);
        Canvas.SetLeft(Toolbar, nx);
        Canvas.SetTop(Toolbar, ny);
        _toolbarMoved = true;
        _toolbarPos = new WpfPoint(nx, ny);
        _toolbarDragStart = pos;
        e.Handled = true;
    }

    private void OnToolbarGripUp(object sender, MouseButtonEventArgs e)
    {
        if (!_toolbarDragging) return;
        _toolbarDragging = false;
        ToolbarGrip.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Sahne kırpma klavye işleme
        if (_canvas != null && _canvas.IsSceneCropping)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                _canvas.CommitSceneCrop();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                _canvas.CancelSceneCrop();
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Escape) return;
        if (_canvas != null && _canvas.IsCropping)
        {
            _canvas.CancelCrop();
            e.Handled = true;
        }
        else if (_textEditing)
        {
            _textBox?.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            CommitTextEdit();
            e.Handled = true;
        }
        else if (_canvas != null && _canvas.Tool != EditorTool.Select)
        {
            _canvas.Tool = EditorTool.Select;
            e.Handled = true;
        }
        else
        {
            Close();
            e.Handled = true;
        }
    }

    // ===================== Pencere algılama =====================
    private void BuildWindowList()
    {
        if (_mode != CaptureMode.Region) return;
        var hwndSelf = new WindowInteropHelper(this).Handle;
        var hwndShell = GetShellWindow();
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        var list = new List<(IntPtr, WpfRect)>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (hwnd == hwndSelf || hwnd == hwndShell) return true;
            if (IsIconic(hwnd)) return true;
            if (GetWindowTextLength(hwnd) == 0) return true;
            // DWM extended frame = gölge hariç görünür kenar (DWMWA_EXTENDED_FRAME_BOUNDS)
            // DWM başarısız olursa GetWindowRect ile fall-back
            WinRect r;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<WinRect>()) != 0)
            {
                if (!GetWindowRect(hwnd, out r)) return true;
            }
            int pw = r.Right - r.Left, ph = r.Bottom - r.Top;
            if (pw <= 0 || ph <= 0) return true;
            double dipX = (r.Left - _virtualBounds.X) / dpi;
            double dipY = (r.Top - _virtualBounds.Y) / dpi;
            double dipW = pw / dpi;
            double dipH = ph / dpi;
            list.Add((hwnd, new WpfRect(dipX, dipY, dipW, dipH)));
            return true;
        }, IntPtr.Zero);

        // Küçük pencereler önce: üstte olan pencereler (daha küçük alan) önce bulunur
        list.Sort((a, b) => (a.Item2.Width * a.Item2.Height).CompareTo(b.Item2.Width * b.Item2.Height));
        _visibleWindows = list;
    }

    private void UpdateWindowHover(WpfPoint pos)
    {
        if (_visibleWindows.Count == 0) return;
        WpfRect found = WpfRect.Empty;
        var overlayBounds = new WpfRect(0, 0, ActualWidth, ActualHeight);
        foreach (var (_, rect) in _visibleWindows)
        {
            if (rect.Contains(pos))
            {
                var clamped = WpfRect.Intersect(rect, overlayBounds);
                if (!clamped.IsEmpty && clamped.Width > 0 && clamped.Height > 0)
                { found = clamped; break; }
            }
        }

        if (found == _hoveredWindowDip) return;
        _hoveredWindowDip = found;

        if (found.IsEmpty || found.Width < 1 || found.Height < 1)
        {
            WinHoverBorder.Visibility = Visibility.Collapsed;
            return;
        }
        WinHoverBorder.Visibility = Visibility.Visible;
        WinHoverBorder.Width = found.Width;
        WinHoverBorder.Height = found.Height;
        Canvas.SetLeft(WinHoverBorder, found.X);
        Canvas.SetTop(WinHoverBorder, found.Y);
    }

    private void DisableWindowHover()
    {
        _windowHoverActive = false;
        _visibleWindows = null!;
        WinHoverBorder.Visibility = Visibility.Collapsed;
        _hoveredWindowDip = WpfRect.Empty;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        MoveWindow(hwnd, _virtualBounds.X, _virtualBounds.Y, _virtualBounds.Width, _virtualBounds.Height, true);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ScreenImage.Width = ActualWidth;
        ScreenImage.Height = ActualHeight;
        UpdateDimRects(WpfRect.Empty);
        BuildModeBar();
        ApplyMode();
        Activate();
        Focus();
        BuildWindowList();
    }

    // ===================== Mod çubuğu =====================
    private void BuildModeBar()
    {
        ModeStack.Children.Clear();
        AddModeButton("IconRegion", "Bölgesel", CaptureMode.Region);
        AddModeButton("IconFullscreen", "Tam ekran", CaptureMode.FullScreen);
        AddModeButton("IconLayers", "Serbest", CaptureMode.Free);
    }

    private void AddModeButton(string icon, string label, CaptureMode mode)
    {
        var btn = new ToggleButton
        {
            IsChecked = _mode == mode, Cursor = Cursors.Hand, Height = 34,
            Padding = new Thickness(10, 0, 12, 0), Margin = new Thickness(1),
            Foreground = System.Windows.Media.Brushes.White, Background = System.Windows.Media.Brushes.Transparent,
            ToolTip = label,
            Style = TryFindResource("ModeChip") as Style,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(icon), Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.8,
            Width = 17, Height = 17, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = System.Windows.Media.Brushes.White });
        btn.Content = sp;
        btn.Click += (_, _) => { _mode = mode; _toolbarMoved = false; ApplyMode(); };
        ModeStack.Children.Add(btn);
    }

    private void ApplyMode()
    {
        // Mod butonlarının seçili halini güncelle.
        int i = 0;
        foreach (ToggleButton b in ModeStack.Children.OfType<ToggleButton>())
            b.IsChecked = (CaptureMode)i++ == _mode;

        // Pencere algılama sadece Region modunda geçerli
        if (_mode != CaptureMode.Region && _windowHoverActive)
            DisableWindowHover();

        ResetSelection();
        // Sürükleme tutamağı sadece tam ekran/serbest modlarda.
        ToolbarGrip.Visibility = _mode == CaptureMode.Region
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        if (_mode == CaptureMode.FullScreen)
        {
            HintText.Text = "Tam ekran — düzenlemeye başlayın";
            _selDip = new WpfRect(0, 0, ActualWidth, ActualHeight);
            _pixelRegion = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
            EnterEditPhase(useScreenshotBackground: true);
        }
        else if (_mode == CaptureMode.Free)
        {
            HintText.Text = "Serbest — Ctrl+V ile resim yapıştırın";
            _selDip = new WpfRect(0, 0, ActualWidth, ActualHeight);
            _pixelRegion = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
            EnterEditPhase(useScreenshotBackground: false);
        }
        else
        {
            HintText.Text = "Alan seçmek için sürükleyin";
            Cursor = Cursors.Cross;
            ActionBar.Visibility = Visibility.Collapsed;
            ModeBar.Visibility = Visibility.Visible;
            HintBox.Visibility = Visibility.Visible;
            // Ölçüm kesinleşsin diye yerleşimi bir sonraki layout turunda yap.
            Dispatcher.BeginInvoke(PositionTopBars, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void PositionTopBars()
    {
        ModeBar.UpdateLayout();
        Canvas.SetLeft(ModeBar, Math.Round((ActualWidth - ModeBar.ActualWidth) / 2));
        Canvas.SetTop(ModeBar, 18);

        HintBox.UpdateLayout();
        Canvas.SetLeft(HintBox, Math.Round((ActualWidth - HintBox.ActualWidth) / 2));
        Canvas.SetTop(HintBox, 18 + ModeBar.ActualHeight + 10);
    }

    // ===================== Faz 1: Seçim (sadece Bölgesel) =====================
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != CaptureMode.Region) return;

        var pos = e.GetPosition(Root);

        // Pencere algılama: ilk tıklamada hover'ı kapat
        // Hover'da pencere varsa önce onu seç; ama kullanıcı sürüklemeye başlarsa
        // serbest seçime geç (OnMouseMove 4px eşiğinde _windowClickPending → false)
        if (_windowHoverActive && _phase == Phase.Select)
        {
            var hovered = _hoveredWindowDip;
            DisableWindowHover();
            if (!hovered.IsEmpty && hovered.Width >= 50 && hovered.Height >= 50)
            {
                // Hemen commit etme — fare bırakıldığında commit olacak
                _windowClickPending = true;
                _windowClickBounds = hovered;
                _dragging = true;
                _start = pos;
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // Edit fazında: kenar resize veya yeni seçim
        if (_phase == Phase.Edit)
        {
            int edge = HitSelectionEdge(pos);
            if (edge >= 0)
            {
                _selResizing = true;
                _selResizeEdge = edge;
                _start = pos;
                EditHost.Visibility = Visibility.Collapsed;
                Toolbar.Visibility = Visibility.Collapsed;
                ActionBar.Visibility = Visibility.Collapsed;
                OptionBar.Visibility = Visibility.Collapsed;
                SelectionBorder.Opacity = 1.0;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            // Dim bölgesine tıklama → yeni seçim HAZIRLIĞI. Mevcut bölgeyi henüz
            // bozma; sadece gerçek bir sürükleme başlarsa (OnMouseMove) yeni seçime geç.
            // Böylece bölge dışına tek tık mevcut seçimi yok etmez.
            if (!_selDip.Contains(pos))
            {
                _pendingNewSelection = true;
                _newSelectionArmed = false;
                _dragging = true;
                _start = pos;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            return;
        }

        if (_phase != Phase.Select) return;
        _dragging = true;
        _start = pos;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Root);

        if (_windowHoverActive && _mode == CaptureMode.Region && _phase == Phase.Select && !_dragging)
            UpdateWindowHover(pos);

        if (_selResizing)
        {
            ResizeSelection(pos);
            return;
        }

        // Edit fazında kenar cursor'ı güncelle
        if (_phase == Phase.Edit && !_dragging && _mode == CaptureMode.Region)
        {
            int edge = HitSelectionEdge(pos);
            if (edge >= 0)
            {
                Cursor = edge switch { 0 or 2 => Cursors.SizeNS, 1 or 3 => Cursors.SizeWE, 4 or 6 => Cursors.SizeNWSE, _ => Cursors.SizeNESW };
                return;
            }
            if (!_selDip.Contains(pos))
                Cursor = Cursors.Cross;
        }

        if (!_dragging) return;

        // Pencere click pending: sürükleme başladıysa serbest seçime geç
        if (_windowClickPending)
        {
            if (Math.Abs(pos.X - _start.X) < 4 && Math.Abs(pos.Y - _start.Y) < 4) return;
            _windowClickPending = false;
            _windowClickBounds = WpfRect.Empty;
        }

        // Yeni seçim hazırlığı: ancak gerçek bir sürükleme başlayınca mevcut bölgeyi bırak.
        if (_pendingNewSelection && !_newSelectionArmed)
        {
            if (Math.Abs(pos.X - _start.X) < 4 && Math.Abs(pos.Y - _start.Y) < 4) return;
            _newSelectionArmed = true;
            Toolbar.Visibility = Visibility.Collapsed;
            ActionBar.Visibility = Visibility.Collapsed;
            OptionBar.Visibility = Visibility.Collapsed;
            EditHost.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            UpdateDimRects(WpfRect.Empty);
        }

        if (_phase == Phase.Select || _pendingNewSelection)
            UpdateSelectionVisual(MakeRect(_start, pos));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Pencere tıklama — sürükleme olmadıysa window bounds'u seç
        if (_windowClickPending)
        {
            _windowClickPending = false;
            _dragging = false;
            ReleaseMouseCapture();
            var bounds = _windowClickBounds;
            _windowClickBounds = WpfRect.Empty;
            if (!bounds.IsEmpty)
            {
                _selDip = bounds;
                _pixelRegion = ToPixelRegion(bounds);
                EnterEditPhase(useScreenshotBackground: true);
            }
            return;
        }

        if (_pendingNewSelection)
        {
            _pendingNewSelection = false;
            _dragging = false;
            bool armed = _newSelectionArmed;
            _newSelectionArmed = false;
            ReleaseMouseCapture();
            var newRect = MakeRect(_start, e.GetPosition(Root));
            if (!armed || newRect.Width < 50 || newRect.Height < 50)
            {
                SizeBadge.Visibility = Visibility.Collapsed;
                EditHost.Visibility = Visibility.Visible;
                SelectionBorder.Visibility = Visibility.Visible;
                UpdateDimRects(_selDip);
                Toolbar.Visibility = Visibility.Visible;
                ActionBar.Visibility = Visibility.Visible;
                // Seçim bölgesi dışına tek tık: seçili öğe + option paneli korunsun.
                BuildOptionBar();
                PositionPanels();
                return;
            }
            LeaveEditPhase();
            _selDip = newRect;
            _pixelRegion = ToPixelRegion(newRect);
            EnterEditPhase(useScreenshotBackground: true);
            return;
        }

        if (_selResizing)
        {
            _selResizing = false;
            _selResizeEdge = -1;
            ReleaseMouseCapture();
            if (_selDip.Width < 50 || _selDip.Height < 50) { LeaveEditPhase(); ResetSelection(); return; }
            _pixelRegion = ToPixelRegion(_selDip);
            RebuildEditForNewRegion();
            return;
        }

        if (_phase != Phase.Select || !_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var rect = MakeRect(_start, e.GetPosition(Root));
        if (rect.Width < 50 || rect.Height < 50) { ResetSelection(); return; }

        _selDip = rect;
        _pixelRegion = ToPixelRegion(rect);
        EnterEditPhase(useScreenshotBackground: true);
    }

    private int HitSelectionEdge(WpfPoint p)
    {
        if (_selDip.Width < 50) return -1;
        const double tol = 7;
        double l = _selDip.X, t = _selDip.Y, r = _selDip.X + _selDip.Width, b = _selDip.Y + _selDip.Height;
        bool nearL = Math.Abs(p.X - l) <= tol && p.Y >= t - tol && p.Y <= b + tol;
        bool nearR = Math.Abs(p.X - r) <= tol && p.Y >= t - tol && p.Y <= b + tol;
        bool nearT = Math.Abs(p.Y - t) <= tol && p.X >= l - tol && p.X <= r + tol;
        bool nearB = Math.Abs(p.Y - b) <= tol && p.X >= l - tol && p.X <= r + tol;
        if (nearT && nearL) return 4; // TL
        if (nearT && nearR) return 5; // TR
        if (nearB && nearR) return 6; // BR
        if (nearB && nearL) return 7; // BL
        if (nearT) return 0;
        if (nearR) return 1;
        if (nearB) return 2;
        if (nearL) return 3;
        return -1;
    }

    private void ResizeSelection(WpfPoint p)
    {
        double l = _selDip.X, t = _selDip.Y, r = _selDip.X + _selDip.Width, b = _selDip.Y + _selDip.Height;
        switch (_selResizeEdge)
        {
            case 0: t = p.Y; break; // top
            case 1: r = p.X; break; // right
            case 2: b = p.Y; break; // bottom
            case 3: l = p.X; break; // left
            case 4: t = p.Y; l = p.X; break; // TL
            case 5: t = p.Y; r = p.X; break; // TR
            case 6: b = p.Y; r = p.X; break; // BR
            case 7: b = p.Y; l = p.X; break; // BL
        }
        _selDip = new WpfRect(Math.Min(l, r), Math.Min(t, b), Math.Abs(r - l), Math.Abs(b - t));
        UpdateSelectionVisual(_selDip);
        PositionPanels();
    }

    private void LeaveEditPhase()
    {
        _phase = Phase.Select;
        Cursor = Cursors.Cross;
        EditHost.Child = null;
        EditHost.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        OptionBar.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        _canvas = null;
        _scene = null;
        ResetSelection();
    }

    private void RebuildEditForNewRegion()
    {
        using var cropped = ScreenCapture.Crop(_screenshot, _pixelRegion);
        var newScene = new Scene { Background = ToSkBitmap(cropped) };
        _scene = newScene;
        _scene.Changed += () => { if (OptionBar.Visibility == Visibility.Visible) PositionOptionBar(); };
        _canvas = new InteractiveCanvas(_scene, _settings.ToolStyles) { Layout = LayoutMode.OneToOne };
        _canvas.TextEditRequested += OnTextEditRequested;
        _canvas.CropRequested += img => _canvas.BeginCrop(img);
        _canvas.SelectionChanged += () => BuildOptionBar();
        _canvas.ToolChanged += () => { SyncToolButtons(); BuildOptionBar(); };
        _canvas.ItemMoved += () => Dispatcher.BeginInvoke(PositionOptionBar, System.Windows.Threading.DispatcherPriority.Render);
        EditHost.Child = _canvas;
        EditHost.Visibility = Visibility.Visible;
        Canvas.SetLeft(EditHost, _selDip.X);
        Canvas.SetTop(EditHost, _selDip.Y);
        EditHost.Width = _selDip.Width;
        EditHost.Height = _selDip.Height;
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.Opacity = 0.35;
        SelectionBorder.Width = _selDip.Width;
        SelectionBorder.Height = _selDip.Height;
        Canvas.SetLeft(SelectionBorder, _selDip.X);
        Canvas.SetTop(SelectionBorder, _selDip.Y);
        Toolbar.Visibility = Visibility.Visible;
        ActionBar.Visibility = Visibility.Visible;
        UpdateDimRects(_selDip);
        PositionPanels();
        _canvas.Focus();
    }

    private static WpfRect MakeRect(WpfPoint a, WpfPoint b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new WpfRect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private void UpdateDimRects(WpfRect hole)
    {
        double w = ActualWidth, h = ActualHeight;
        if (hole.IsEmpty || hole.Width <= 0 || hole.Height <= 0)
        {
            DimTop.Width = w; DimTop.Height = h;
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);
            DimBottom.Height = 0; DimLeft.Width = 0; DimRight.Width = 0;
            return;
        }
        // Top
        DimTop.Width = w; DimTop.Height = Math.Max(0, hole.Y);
        Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);
        // Bottom
        double botY = hole.Y + hole.Height;
        DimBottom.Width = w; DimBottom.Height = Math.Max(0, h - botY);
        Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, botY);
        // Left
        DimLeft.Width = Math.Max(0, hole.X); DimLeft.Height = hole.Height;
        Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, hole.Y);
        // Right
        double rightX = hole.X + hole.Width;
        DimRight.Width = Math.Max(0, w - rightX); DimRight.Height = hole.Height;
        Canvas.SetLeft(DimRight, rightX); Canvas.SetTop(DimRight, hole.Y);
    }

    private void UpdateSelectionVisual(WpfRect rect)
    {
        UpdateDimRects(rect);
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.Opacity = 1.0;
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);

        var px = ToPixelRegion(rect);
        SizeText.Text = $"{px.Width} × {px.Height}";
        SizeBadge.Visibility = Visibility.Visible;
        double by = rect.Y - 26 < 0 ? rect.Y + 6 : rect.Y - 26;
        Canvas.SetLeft(SizeBadge, rect.X);
        Canvas.SetTop(SizeBadge, by);
    }

    private void ResetSelection()
    {
        UpdateDimRects(WpfRect.Empty);
        SelectionBorder.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
    }

    private Rectangle ToPixelRegion(WpfRect rect)
    {
        double sx = _screenshot.Width / ActualWidth;
        double sy = _screenshot.Height / ActualHeight;
        var r = new Rectangle(
            (int)Math.Round(rect.X * sx), (int)Math.Round(rect.Y * sy),
            (int)Math.Round(rect.Width * sx), (int)Math.Round(rect.Height * sy));
        r.Intersect(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        return r;
    }

    // ===================== Faz 2: Düzenleme =====================
    private void EnterEditPhase(bool useScreenshotBackground)
    {
        _phase = Phase.Edit;
        Cursor = Cursors.Arrow;
        HintBox.Visibility = Visibility.Collapsed;
        ModeBar.Visibility = Visibility.Collapsed;   // mod çubuğu sadece seçim öncesi
        SizeBadge.Visibility = Visibility.Collapsed;

        bool free = _mode == CaptureMode.Free;
        if (free)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            UpdateDimRects(WpfRect.Empty);
        }
        else
        {
            SelectionBorder.Visibility = Visibility.Visible;
            SelectionBorder.Opacity = 0.35;
                UpdateDimRects(_selDip);
            SelectionBorder.Width = _selDip.Width;
            SelectionBorder.Height = _selDip.Height;
            Canvas.SetLeft(SelectionBorder, _selDip.X);
            Canvas.SetTop(SelectionBorder, _selDip.Y);
        }

        // Sahne
        bool reusedFreeScene = false;
        if (free)
        {
            // Serbest sahne oturum boyunca App'te saklanır (Esc sonrası korunur).
            if (FreeSceneProvider?.Invoke() is { } existing)
            {
                _scene = existing;
                reusedFreeScene = true;
            }
            else
            {
                _scene = new Scene { CanvasSize = new SKSize((float)_selDip.Width, (float)_selDip.Height) };
                FreeSceneSink?.Invoke(_scene);
            }
        }
        else
        {
            using var cropped = ScreenCapture.Crop(_screenshot, _pixelRegion);
            _scene = new Scene { Background = ToSkBitmap(cropped) };
        }
        // Öğe taşınınca/değişince seçenek şeridi öğeyi takip etsin.
        _scene.Changed += () => { if (OptionBar.Visibility == Visibility.Visible) PositionOptionBar(); };

        _canvas = new InteractiveCanvas(_scene, _settings.ToolStyles) { Layout = LayoutMode.OneToOne };
        _canvas.TextEditRequested += OnTextEditRequested;
        _canvas.CropRequested += img => _canvas.BeginCrop(img);
        _canvas.SelectionChanged += () => BuildOptionBar();
        _canvas.ToolChanged += () => { SyncToolButtons(); BuildOptionBar(); };
        _canvas.ItemMoved += () => Dispatcher.BeginInvoke(PositionOptionBar, System.Windows.Threading.DispatcherPriority.Render);

        EditHost.Child = _canvas;
        EditHost.Visibility = Visibility.Visible;
        Canvas.SetLeft(EditHost, _selDip.X);
        Canvas.SetTop(EditHost, _selDip.Y);
        EditHost.Width = _selDip.Width;
        EditHost.Height = _selDip.Height;

        bool fullLike = free || _mode == CaptureMode.FullScreen;
        ToolStack.Orientation = fullLike ? Orientation.Horizontal : Orientation.Vertical;

        BuildToolbar();
        BuildActionBar();
        BuildOptionBar();
        PositionPanels();

        // Serbest mod placeholder
        if (free)
        {
            SyncPlaceholder();
            _scene.Changed += SyncPlaceholder;
        }

        // Serbest mod ilk açılışta panodaki resmi yerleştir (yeniden kullanılan sahnede değil).
        if (free && !reusedFreeScene) TryPasteImage();
        _canvas.Focus();
    }

    /// <summary>App, serbest sahneyi sağlamak/saklamak için bunları bağlar (oturum belleği).</summary>
    public Func<Scene?>? FreeSceneProvider { get; set; }
    public Action<Scene>? FreeSceneSink { get; set; }

    private static SKBitmap ToSkBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var sk = new SKBitmap(info);
            unsafe { Buffer.MemoryCopy((void*)data.Scan0, (void*)sk.GetPixels(), sk.ByteCount, data.Stride * bmp.Height); }
            return sk;
        }
        finally { bmp.UnlockBits(data); }
    }

    // ---- Araç şeridi (dikey, sadece araçlar) ----
    private static readonly (EditorTool tool, string icon, string name, string key)[] Tools =
    {
        (EditorTool.Select,    "IconCursor",      "Seç / Taşı", "V"),
        (EditorTool.Rectangle, "IconSquare",      "Dikdörtgen", "R"),
        (EditorTool.Ellipse,   "IconCircle",      "Elips",      "O"),
        (EditorTool.Arrow,     "IconArrow",       "Ok",         "A"),
        (EditorTool.Line,      "IconLine",        "Çizgi",      "L"),
        (EditorTool.Pen,       "IconPen",         "Kalem",      "P"),
        (EditorTool.Highlight, "IconHighlighter", "Fosforlu",   "H"),
        (EditorTool.Text,      "IconText",        "Metin",      "T"),
        (EditorTool.Step,      "IconStep",        "Adım",       "S"),
        (EditorTool.Blur,      "IconBlur",        "Bulanıklaştır", "B"),
    };

    private void BuildToolbar()
    {
        ToolStack.Children.Clear();
        _toolButtons.Clear();
        foreach (var (tool, icon, name, key) in Tools)
        {
            var btn = new ToggleButton
            {
                Style = (Style)FindResource("IconToolButton"),
                Tag = FindResource(icon),
                IsChecked = tool == (_canvas?.Tool ?? EditorTool.Select),
            };
            btn.Click += (_, _) => { if (_canvas != null) _canvas.Tool = tool; };
            AttachHint(btn, $"{name}  [{key}]");
            _toolButtons[tool] = btn;
            ToolStack.Children.Add(btn);
        }
        // Serbest modda sahne kırpma butonu
        if (_mode == CaptureMode.Free)
        {
            // Horizontal toolbar → dikey çizgi; Vertical → yatay çizgi
            bool horiz = ToolStack.Orientation == Orientation.Horizontal;
            var sep = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x52, 0x66)),
            };
            if (horiz) { sep.Width = 1; sep.Height = 22; sep.Margin = new Thickness(4, 0, 4, 0); }
            else        { sep.Height = 1; sep.Margin = new Thickness(4, 4, 4, 4); }
            ToolStack.Children.Add(sep);

            var cropBtn = new Button { Style = TryFindResource("IconCmdButton") as Style };
            cropBtn.Tag = TryFindResource("IconCrop");
            // IconToolButton ile aynı 32×32
            cropBtn.Width = 32; cropBtn.Height = 32; cropBtn.Margin = new Thickness(2);
            AttachHint(cropBtn, "Sahneyi Kırp  [C]");
            cropBtn.Click += (_, _) => _canvas?.BeginSceneCrop();
            ToolStack.Children.Add(cropBtn);
        }

        Toolbar.Visibility = Visibility.Visible;
    }

    private void SyncToolButtons()
    {
        foreach (var (tool, btn) in _toolButtons)
            btn.IsChecked = tool == _canvas?.Tool;
    }

    // ---- Seçenek şeridi (yatay, seçimin altında) ----
    private void BuildOptionBar()
    {
        if (_canvas == null) return;
        OptionStack.Children.Clear();
        var style = _settings.ToolStyles;
        var sel = _canvas.SelectedItem;
        var all = _canvas.Selection;
        var tool = _canvas.Tool;

        if (sel == null)
        {
            OptionBar.Visibility = Visibility.Collapsed;
            return;
        }

        bool multi = all.Count > 1;
        bool allSameType = multi && all.All(s => s.GetType() == all[0].GetType());

        bool hasStrokeable = all.Any(s => s is RectItem or EllipseItem or LineItem or FreehandItem);
        bool hasFillable = all.Any(s => s is RectItem or EllipseItem);
        bool allText = allSameType && sel is TextItem;
        bool allStep = allSameType && sel is StepItem;
        bool allArrow = allSameType && sel is ArrowItem;
        bool allBlur = allSameType && sel is BlurItem;
        bool anyColorable = all.Any(s => s is not BlurItem and not ImageItem);

        // Renk paleti
        bool showColor = !multi
            ? ((tool != EditorTool.Select && tool != EditorTool.Blur) || (sel is not BlurItem and not ImageItem))
            : anyColorable;
        if (showColor)
        {
            AddColorRow(c =>
            {
                string hex = InteractiveCanvas.HexFromColor(c);
                if (!multi)
                {
                    if (tool == EditorTool.Step || sel is StepItem)
                    { if (sel is StepItem si) si.BadgeColor = c; style.StepColor = hex; }
                    else
                    { sel.StrokeColor = c; if (tool == EditorTool.Text) style.TextColor = hex; else style.StrokeColor = hex; }
                }
                else
                {
                    foreach (var s in all)
                    {
                        if (s is StepItem si) si.BadgeColor = c;
                        else if (s is not BlurItem and not ImageItem) s.StrokeColor = c;
                    }
                    style.StrokeColor = hex;
                }
                _scene?.RaiseChanged();
            });
        }

        // Dolgu rengi — Rect ve Ellipse
        bool fillRelevant = !multi
            ? (tool is EditorTool.Rectangle or EditorTool.Ellipse || sel is RectItem or EllipseItem)
            : hasFillable;
        if (fillRelevant)
        {
            AddSep();
            var curFill = sel?.FillColor ?? SKColors.Transparent;
            var strokeForFill = sel?.StrokeColor ?? InteractiveCanvas.ColorFromHex(style.StrokeColor);
            AddFillSwatch(curFill, strokeForFill, c =>
            {
                foreach (var s in all)
                    if (s is RectItem or EllipseItem) s.FillColor = c;
                style.FillColor = InteractiveCanvas.HexFromColor(c);
                _scene?.RaiseChanged();
            });
        }

        // Kalınlık slider
        bool strokeRelevant = !multi
            ? (tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Line
                or EditorTool.Arrow or EditorTool.Pen or EditorTool.Highlight
                || sel is RectItem or EllipseItem or LineItem or FreehandItem)
            : hasStrokeable;
        if (strokeRelevant)
        {
            AddSep();
            AddSlider(null, sel?.StrokeWidth ?? style.StrokeWidth, 1, 24, v =>
            {
                foreach (var s in all)
                    if (s is RectItem or EllipseItem or LineItem or FreehandItem) s.StrokeWidth = (float)v;
                style.StrokeWidth = v; _scene?.RaiseChanged();
            });
        }

        // Ok uç boyutu — tek veya hepsi ok
        if (!multi ? (tool == EditorTool.Arrow || sel is ArrowItem) : allArrow)
        {
            AddSep();
            AddSlider(null, (sel as ArrowItem)?.HeadScale ?? style.ArrowHeadScale, 0.5, 3, v =>
            {
                foreach (var s in all)
                    if (s is ArrowItem a) a.HeadScale = (float)v;
                style.ArrowHeadScale = v; _scene?.RaiseChanged();
            });
        }

        // Metin — tek veya hepsi metin
        if (!multi ? (tool == EditorTool.Text || sel is TextItem) : allText)
        {
            AddSep();
            AddChip("B", (sel as TextItem)?.Bold ?? style.FontBold, b =>
            {
                foreach (var s in all) if (s is TextItem ti) ti.Bold = b;
                style.FontBold = b; _scene?.RaiseChanged();
            }, bold: true);
            AddChip("I", (sel as TextItem)?.Italic ?? false, b =>
            {
                foreach (var s in all) if (s is TextItem ti) ti.Italic = b;
                _scene?.RaiseChanged();
            }, italic: true);
            AddSep();
            AddFontCombo();
            AddSep();
            AddEffectToggle(BuildShadowIcon(), "Gölge", (sel as TextItem)?.Shadow ?? style.TextShadow, b =>
            {
                foreach (var s in all) if (s is TextItem ti) ti.Shadow = b;
                style.TextShadow = b; _scene?.RaiseChanged();
            });
            AddEffectToggle(BuildStrokeIcon(), "Kontur", (sel as TextItem)?.StrokeText ?? style.TextStroke, b =>
            {
                foreach (var s in all) if (s is TextItem ti) ti.StrokeText = b;
                style.TextStroke = b; _scene?.RaiseChanged();
            });
            AddRibbonToggle(sel as TextItem, style);
        }

        // Step — tek veya hepsi step
        if (!multi ? (tool == EditorTool.Step || sel is StepItem) : allStep)
        {
            AddSep();
            var cur = (sel as StepItem)?.Shape ?? style.StepShape;
            var shapeButtons = new List<ToggleButton>();
            foreach (var (icon, tip, shape) in new[] {
                ("IconStepCircle", "Daire", StepShape.Circle),
                ("IconStepSquare", "Kare", StepShape.Square),
                ("IconStepBubble", "Balon", StepShape.Bubble) })
            {
                var s = shape;
                var btn = MakeIconChipButton(icon, tip, cur == s);
                btn.Click += (_, _) =>
                {
                    foreach (var it in all) if (it is StepItem si) si.Shape = s;
                    style.StepShape = s;
                    foreach (var ob in shapeButtons) ob.IsChecked = false;
                    btn.IsChecked = true; _scene?.RaiseChanged();
                };
                shapeButtons.Add(btn);
                OptionStack.Children.Add(btn);
            }
            AddSep();
            var resetBtn = MakeIconChipButton("", "Sıfırla", false);
            resetBtn.IsChecked = false;
            if (resetBtn.Content is Viewbox) resetBtn.Content = null;
            resetBtn.Content = new TextBlock
            {
                Text = "↺ Sıfırla", FontSize = 10.5,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA4, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            resetBtn.Width = double.NaN;
            resetBtn.Click += (_, _) =>
            {
                if (_scene == null) return;
                _scene.ResetStepCounter();
                resetBtn.IsChecked = false;
            };
            AttachHint(resetBtn, "Sayacı sıfırla (sonraki adım 1'den başlar)");
            OptionStack.Children.Add(resetBtn);
        }

        // Blur — tek veya hepsi blur
        if (!multi ? (tool == EditorTool.Blur || sel is BlurItem) : allBlur)
        {
            AddSep();
            AddSlider(null, (sel as BlurItem)?.Strength ?? style.BlurStrength, 2, 8, v =>
            {
                foreach (var s in all) if (s is BlurItem bi) bi.Strength = (float)v;
                style.BlurStrength = v; _scene?.RaiseChanged();
            });
            AddChip("Piksel", (sel as BlurItem)?.Pixelate ?? style.BlurPixelate, b =>
            {
                foreach (var s in all) if (s is BlurItem bi) bi.Pixelate = b;
                style.BlurPixelate = b; _scene?.RaiseChanged();
            });
        }

        // Baştaki separator(lar)ı kaldır
        while (OptionStack.Children.Count > 0 && OptionStack.Children[0] is Border sep && sep.Width == 1)
            OptionStack.Children.RemoveAt(0);

        OptionBar.Visibility = OptionStack.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(PositionPanels, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- Üst aksiyon çubuğu (Kopyala/Kaydet/Yükle) ----
    private int _gifFps = 15;

    private void BuildActionBar()
    {
        ActionStack.Children.Clear();
        if (_mode == CaptureMode.Region)
            ActionStack.Children.Add(MakeGifSplitButton());
        ActionStack.Children.Add(MakeCmd("IconCopy", "Kopyala", "Kopyala (Ctrl+C)", DoCopy));
        ActionStack.Children.Add(MakeCmd("IconSave", "Kaydet", "Kaydet (Ctrl+S)", DoSave));
        ActionStack.Children.Add(MakeCmd("IconCloud", "Yükle", "Buluta Yükle", DoUpload, accent: true));
        if (_mode == CaptureMode.Free)
            ActionStack.Children.Add(MakeCmd("IconTrash", "Temizle", "Sahneyi temizle", DoClearScene));
        ActionStack.Children.Add(MakeCmd("IconClose", "Kapat", "Kapat (Esc)", () => Close()));
        ActionBar.Visibility = Visibility.Visible;
    }

    private FrameworkElement MakeGifSplitButton()
    {
        // Ana buton — GIF Kaydet
        var mainBtn = new Button
        {
            Cursor = Cursors.Hand, Height = 34,
            Padding = new Thickness(10, 0, 6, 0), Margin = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.White,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Style = TryFindResource("ActionChip") as Style,
        };
        var mainSp = new StackPanel { Orientation = Orientation.Horizontal };
        mainSp.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("IconRecord"),
            Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.8,
            Width = 17, Height = 17, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        var mainLabel = new TextBlock
        {
            Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13, Foreground = System.Windows.Media.Brushes.White,
        };
        mainLabel.Text = $"GIF  {_gifFps}fps";
        mainSp.Children.Add(mainLabel);
        mainBtn.Content = mainSp;
        mainBtn.Click += (_, _) => OnGifRecord();
        AttachHint(mainBtn, "GIF kaydını başlat");

        // Ok butonu — FPS seç
        var arrowBtn = new Button
        {
            Cursor = Cursors.Hand, Height = 34, Width = 22,
            Padding = new Thickness(0), Margin = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.White,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Style = TryFindResource("ActionChip") as Style,
            Content = new TextBlock { Text = "▾", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
        };
        AttachHint(arrowBtn, "FPS seç");

        // FPS context menu
        void ShowFpsMenu()
        {
            var menu = new ContextMenu();
            foreach (int fps in new[] { 5, 10, 12, 15, 20, 24, 30 })
            {
                int f = fps;
                var item = new MenuItem
                {
                    Header = $"{fps} FPS",
                    IsChecked = _gifFps == fps,
                };
                item.Click += (_, _) =>
                {
                    _gifFps = f;
                    mainLabel.Text = $"GIF  {_gifFps}fps";
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = arrowBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        arrowBtn.Click += (_, _) => ShowFpsMenu();

        // Birleştir — sol main, sağ arrow
        var grid = new Grid { Margin = new Thickness(1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(mainBtn, 0);
        Grid.SetColumn(arrowBtn, 1);
        grid.Children.Add(mainBtn);
        grid.Children.Add(arrowBtn);
        return grid;
    }

    private void OnGifRecord()
    {
        // ToPixelRegion screenshot oranı kullanır; GIF için DPI-aware piksel koordinatı lazım
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var gifPixelRegion = new System.Drawing.Rectangle(
            (int)Math.Round(_selDip.X      * dpi.DpiScaleX) + _virtualBounds.X,
            (int)Math.Round(_selDip.Y      * dpi.DpiScaleY) + _virtualBounds.Y,
            (int)Math.Round(_selDip.Width  * dpi.DpiScaleX),
            (int)Math.Round(_selDip.Height * dpi.DpiScaleY));
        var dipRegion = _selDip;
        Close();

        var recorder = new Gif.GifRecorder(gifPixelRegion, fps: _gifFps);
        var overlay = new GifRecordingOverlayWindow(recorder, dipRegion);
        overlay.Stopped += OnGifStopped;
        overlay.Show();
        recorder.Start();
    }

    private static void OnGifStopped(Gif.GifRecorder recorder)
    {
        var preview = new GifPreviewWindow(recorder);
        preview.Show();
    }

    // Temel 6 renk
    private static readonly string[] Palette =
    {
        "#FFE5484D", // kırmızı
        "#FFF2D600", // sarı
        "#FF2FBF71", // yeşil
        "#FF2F6FED", // mavi
        "#FF000000", // siyah
        "#FFFFFFFF", // beyaz
    };

    private void AddColorRow(Action<SKColor> onPick)
    {
        var target = OptionStack;
        foreach (var hex in Palette)
        {
            var c = InteractiveCanvas.ColorFromHex(hex);
            var sw = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(1.5, 0, 1.5, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x5E, 0x70)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, ToolTip = hex,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sw.MouseLeftButtonDown += (_, _) => onPick(c);
            target.Children.Add(sw);
        }

        var plus = new Button
        {
            Width = 22, Height = 22, Margin = new Thickness(3, 0, 1, 0), Cursor = Cursors.Hand,
            ToolTip = "Özel renk", Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = BuildPaletteIcon(16),
        };
        plus.Click += (_, _) =>
        {
            var init = InteractiveCanvas.ColorFromHex(_settings.ToolStyles.StrokeColor);
            var wpfInit = System.Windows.Media.Color.FromRgb(init.Red, init.Green, init.Blue);
            var picker = new ColorPickerPopup(plus, wpfInit, wc => onPick(new SKColor(wc.R, wc.G, wc.B, 255)));
            picker.Open();
        };
        target.Children.Add(plus);
    }

    private void AddSlider(string? label, double value, double min, double max, Action<double> onChange)
    {
        if (label != null)
            OptionStack.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA4, 0xB8)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) });
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 56, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 1, 0) };
        if (TryFindResource("ToolSlider") is Style sliderStyle) s.Style = sliderStyle;
        var valLabel = new TextBlock { Text = ((int)Math.Round(s.Value)).ToString(), Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA4, 0xB8)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, MinWidth = 14, Margin = new Thickness(0, 0, 1, 0) };
        s.ValueChanged += (_, e) => { onChange(e.NewValue); valLabel.Text = ((int)Math.Round(e.NewValue)).ToString(); };
        OptionStack.Children.Add(s);
        OptionStack.Children.Add(valLabel);
    }

    private static readonly string[] Fonts = { "Segoe UI", "Arial", "Calibri", "Consolas", "Comic Sans MS", "Georgia", "Impact", "Verdana" };

    private void AddFontCombo()
    {
        var target = OptionStack;
        var combo = new ComboBox
        {
            Width = 108, Height = 24, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Style = TryFindResource("DarkComboBox") as Style,
        };
        foreach (var f in Fonts) combo.Items.Add(f);
        combo.SelectedItem = (_canvas?.SelectedItem as TextItem)?.FontFamily ?? _settings.ToolStyles.FontFamily;
        if (combo.SelectedItem == null) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            string fam = combo.SelectedItem?.ToString() ?? "Segoe UI";
            if (_canvas?.SelectedItem is TextItem ti) ti.FontFamily = fam; else _settings.ToolStyles.FontFamily = fam;
            _scene?.RaiseChanged();
        };
        target.Children.Add(combo);
    }

    private void AddChip(string label, bool active, Action<bool> onToggle, bool bold = false, bool italic = false)
    {
        var target = OptionStack;
        var b = new ToggleButton
        {
            Content = label, IsChecked = active, Cursor = Cursors.Hand,
            MinWidth = 24, Height = 24, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(5, 0, 5, 0),
            Foreground = System.Windows.Media.Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("ModeChip") as Style,
        };
        if (bold) b.FontWeight = FontWeights.Bold;
        if (italic) b.FontStyle = FontStyles.Italic;
        b.Click += (_, _) => onToggle(b.IsChecked == true);
        AttachHint(b, label);
        target.Children.Add(b);
    }

    private void AddMiniColorSwatch(SKColor color, Action<SKColor> onPick)
    {
        var target = OptionStack;
        var sw = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x5E, 0x70)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
        };
        sw.MouseLeftButtonDown += (_, _) =>
        {
            var wpfInit = System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
            var picker = new ColorPickerPopup(sw, wpfInit, wc =>
            {
                var skc = new SKColor(wc.R, wc.G, wc.B, 255);
                sw.Background = new SolidColorBrush(wc);
                onPick(skc);
            });
            picker.Open();
        };
        target.Children.Add(sw);
    }

    private void AddLevelChips(int current, Action<int> onChange)
    {
        var target = OptionStack;
        var labels = new[] { "S", "M", "L" };
        var buttons = new List<ToggleButton>();
        for (int i = 0; i < 3; i++)
        {
            int lvl = i;
            var b = new ToggleButton
            {
                Content = labels[i], IsChecked = lvl == current, Cursor = Cursors.Hand,
                Width = 22, Height = 22, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Style = TryFindResource("ModeChip") as Style,
            };
            b.Click += (_, _) =>
            {
                foreach (var ob in buttons) ob.IsChecked = false;
                b.IsChecked = true;
                onChange(lvl);
            };
            buttons.Add(b);
            target.Children.Add(b);
        }
    }

    private ToggleButton MakeIconChipButton(string iconKey, string tooltip, bool active, bool filled = false)
    {
        var geo = TryFindResource(iconKey) as System.Windows.Media.Geometry;
        var path = new System.Windows.Shapes.Path
        {
            Data = geo, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = filled ? 1.5 : 1.8,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent,
            Width = 24, Height = 24, Stretch = Stretch.Uniform,
        };
        var tb = new ToggleButton
        {
            Content = new Viewbox { Width = 14, Height = 14, Child = path },
            IsChecked = active, Cursor = Cursors.Hand,
            Width = 26, Height = 26, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("ModeChip") as Style,
        };
        AttachHint(tb, tooltip);
        return tb;
    }

    private void AddEffectToggle(UIElement icon, string tooltip, bool active, Action<bool> onToggle)
    {
        var btn = new ToggleButton
        {
            Content = icon, IsChecked = active, Cursor = Cursors.Hand,
            Width = 28, Height = 28, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("ModeChip") as Style,
        };
        btn.Click += (_, _) => onToggle(btn.IsChecked == true);
        AttachHint(btn, tooltip);
        OptionStack.Children.Add(btn);
    }

    private void AddRibbonToggle(TextItem? sel, ToolStyleMemory style)
    {
        bool active = sel?.Ribbon ?? style.TextRibbon;
        var ribbonSk = sel?.RibbonColor ?? InteractiveCanvas.ColorFromHex(style.TextRibbonColor);

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(BuildRibbonIcon());
        if (active)
        {
            var swatch = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(3, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(ribbonSk.Alpha, ribbonSk.Red, ribbonSk.Green, ribbonSk.Blue)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x5E, 0x70)), BorderThickness = new Thickness(0.5),
            };
            sp.Children.Add(swatch);
        }

        var btn = new ToggleButton
        {
            Content = sp, IsChecked = active, Cursor = Cursors.Hand,
            Height = 28, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("ModeChip") as Style,
        };
        btn.Click += (_, _) =>
        {
            bool nowOn = btn.IsChecked == true;
            if (sel != null) sel.Ribbon = nowOn;
            style.TextRibbon = nowOn;
            _scene?.RaiseChanged();
            if (nowOn)
            {
                var initWpf = System.Windows.Media.Color.FromArgb(ribbonSk.Alpha, ribbonSk.Red, ribbonSk.Green, ribbonSk.Blue);
                var picker = new ColorPickerPopup(btn, initWpf, wc =>
                {
                    var sk = new SKColor(wc.R, wc.G, wc.B, 200);
                    if (sel != null) sel.RibbonColor = sk;
                    style.TextRibbonColor = InteractiveCanvas.HexFromColor(sk);
                    _scene?.RaiseChanged();
                    BuildOptionBar();
                });
                picker.Open();
            }
            else BuildOptionBar();
        };
        AttachHint(btn, "Arkaplan  [Tıkla = aç/kapat + renk seç]");
        OptionStack.Children.Add(btn);
    }

    // Gölge ikonu: arka kare (koyu) + ön kare (açık), offset — referanstaki gibi
    private static UIElement BuildShadowIcon()
    {
        var canvas = new System.Windows.Controls.Canvas { Width = 16, Height = 16 };
        canvas.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
        });
        System.Windows.Controls.Canvas.SetLeft(canvas.Children[0], 4);
        System.Windows.Controls.Canvas.SetTop(canvas.Children[0], 4);
        canvas.Children.Add(new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
        });
        System.Windows.Controls.Canvas.SetLeft(canvas.Children[1], 1);
        System.Windows.Controls.Canvas.SetTop(canvas.Children[1], 1);
        return canvas;
    }

    // Kontur ikonu: iç içe iki kare çerçeve
    private static UIElement BuildStrokeIcon()
    {
        var grid = new Grid { Width = 16, Height = 16 };
        grid.Children.Add(new Border
        {
            Width = 15, Height = 15, CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1.5), Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        grid.Children.Add(new Border
        {
            Width = 9, Height = 9, CornerRadius = new CornerRadius(1.5),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1), Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    // Arkaplan/Şerit ikonu: checkerboard pattern kare
    private static UIElement BuildRibbonIcon()
    {
        var grid = new Grid { Width = 16, Height = 16 };
        var canvas = new System.Windows.Controls.Canvas { Width = 14, Height = 14, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var white = System.Windows.Media.Brushes.White;
        var gray = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                var rect = new System.Windows.Shapes.Rectangle { Width = 5, Height = 5, Fill = (r + c) % 2 == 0 ? white : gray };
                System.Windows.Controls.Canvas.SetLeft(rect, c * 5);
                System.Windows.Controls.Canvas.SetTop(rect, r * 5);
                canvas.Children.Add(rect);
            }
        var border = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0.8), Child = canvas,
            ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(border);
        return grid;
    }

    private void AddFillSwatch(SKColor current, SKColor strokeFallback, Action<SKColor> onPick)
    {
        bool isTransparent = current.Alpha < 10;
        UIElement inner;
        if (isTransparent)
        {
            var cv = new System.Windows.Controls.Canvas { Width = 16, Height = 16, ClipToBounds = true };
            var wb = System.Windows.Media.Brushes.White;
            var gb = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
            for (int r = 0; r < 4; r++)
                for (int c2 = 0; c2 < 4; c2++)
                {
                    var sq = new System.Windows.Shapes.Rectangle { Width = 4, Height = 4, Fill = (r + c2) % 2 == 0 ? wb : gb };
                    System.Windows.Controls.Canvas.SetLeft(sq, c2 * 4);
                    System.Windows.Controls.Canvas.SetTop(sq, r * 4);
                    cv.Children.Add(sq);
                }
            inner = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x5E, 0x70)), BorderThickness = new Thickness(1), Child = cv, ClipToBounds = true };
        }
        else
        {
            inner = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(current.Alpha, current.Red, current.Green, current.Blue)), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x5E, 0x70)), BorderThickness = new Thickness(1) };
        }
        var btn = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Content = inner, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) { Name = "cp" },
            },
        };
        btn.Click += (_, _) =>
        {
            if (!isTransparent)
            {
                onPick(SKColors.Transparent);
                BuildOptionBar();
                return;
            }
            var curStroke = _canvas?.SelectedItem?.StrokeColor ?? InteractiveCanvas.ColorFromHex(_settings.ToolStyles.StrokeColor);
            var fillColor = new SKColor(curStroke.Red, curStroke.Green, curStroke.Blue, 255);
            onPick(fillColor);
            BuildOptionBar();
            var initColor = System.Windows.Media.Color.FromRgb(curStroke.Red, curStroke.Green, curStroke.Blue);
            var picker = new ColorPickerPopup(btn, initColor, wc =>
            {
                onPick(new SKColor(wc.R, wc.G, wc.B, 255));
                BuildOptionBar();
            });
            picker.Open();
        };
        AttachHint(btn, "Dolgu  [Tıkla = aç/kapat + renk seç]");
        OptionStack.Children.Add(btn);
    }

    // Renk tekerleği palette ikonu — SVG'deki 8 renk dilimi + ortadaki gri halka
    private static System.Windows.Controls.Image BuildPaletteIcon(double size)
    {
        // 512x512 viewBox → normalize 0..1, sonra size ile scale
        double s = size / 512.0;
        var dg = new DrawingGroup();
        void Slice(string pathData, byte r, byte g, byte b)
        {
            var geo = Geometry.Parse(pathData).Clone(); // frozen olabilir — klonla
            geo.Transform = new ScaleTransform(s, s);
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)), null, geo));
        }
        // Gri dış halka + iç boşluk
        Slice("M256,0C114.615,0,0,114.615,0,256s114.615,256,256,256s256-114.615,256-256S397.385,0,256,0z M256,336.842c-44.648,0-80.842-36.194-80.842-80.842s36.194-80.842,80.842-80.842s80.842,36.194,80.842,80.842S300.648,336.842,256,336.842z", 0xD8, 0xD8, 0xDA);
        // Mor dilim (sol üst)
        Slice("M282.947,188.632h220.076C485.09,122.726,441.507,67.394,383.64,34.044L229.053,188.632H282.947z", 0xD4, 0xB6, 0xE6);
        // Pembe dilim
        Slice("M229.053,188.632L383.639,34.044C346.068,12.39,302.482,0,256,0c-23.319,0-45.899,3.135-67.368,8.978v220.075L229.053,188.632z", 0xEB, 0xAF, 0xD1);
        // Kırmızı dilim
        Slice("M188.632,229.053V8.978C122.726,26.91,67.394,70.493,34.045,128.36l154.586,154.588V229.053z", 0xE0, 0x71, 0x88);
        // Açık mavi dilim
        Slice("M503.024,188.632H282.947v0.001h0.958l39.463,40.42L477.955,383.64C499.611,346.068,512,302.482,512,256C512,232.681,508.865,210.099,503.024,188.632z", 0xB4, 0xD8, 0xF1);
        // Turkuaz dilim
        Slice("M323.368,282.947v220.075c65.905-17.932,121.238-61.517,154.586-119.382L323.368,229.053V282.947z", 0xAC, 0xFF, 0xF4);
        // Yeşil dilim
        Slice("M282.947,323.368L128.361,477.956C165.932,499.61,209.518,512,256,512c23.319,0,45.899-3.135,67.368-8.977V282.947L282.947,323.368z", 0x95, 0xD5, 0xA7);
        // Sarı dilim
        Slice("M229.053,323.368H8.976C26.91,389.274,70.493,444.606,128.36,477.956l154.588-154.588H229.053z", 0xF8, 0xE9, 0x9B);
        // Turuncu dilim
        Slice("M188.632,282.947L34.045,128.36C12.389,165.932,0,209.518,0,256c0,23.319,3.135,45.901,8.976,67.368h220.076L188.632,282.947z", 0xEF, 0xC2, 0x7B);
        // Koyu mor üst dilim
        Slice("M503.024,188.632C485.09,122.726,441.507,67.394,383.64,34.044L256,161.684v26.947h26.947H503.024z", 0xB6, 0x81, 0xD5);
        // Açık pembe üst dilim
        Slice("M383.639,34.044C346.068,12.39,302.482,0,256,0v161.684L383.639,34.044z", 0xE5, 0x92, 0xBF);
        // Açık yeşil sağ alt
        Slice("M256,350.316V512c23.319,0,45.899-3.135,67.368-8.977V282.947l-40.421,40.421L256,350.316z", 0x80, 0xCB, 0x93);
        // Sarı küçük
        Slice("M282.947,323.368 L256,323.368 L256,350.316 Z", 0xF6, 0xE2, 0x7D);

        dg.ClipGeometry = new EllipseGeometry(new System.Windows.Point(size / 2, size / 2), size / 2 + 0.5, size / 2 + 0.5);
        var di = new DrawingImage(dg);
        di.Freeze();
        return new System.Windows.Controls.Image { Source = di, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    }

    private void AddSep()
    {
        OptionStack.Children.Add(new Border
        {
            Width = 1, Height = 18, Margin = new Thickness(4, 0, 4, 0),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x42, 0x54)),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private Button MakeCmd(string icon, string label, string tip, Action onClick, bool accent = false)
    {
        var b = new Button
        {
            Cursor = Cursors.Hand, Height = 34,
            Padding = new Thickness(10, 0, 12, 0), Margin = new Thickness(1),
            Foreground = System.Windows.Media.Brushes.White,
            Background = accent ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0x6F, 0x12)) : System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Style = TryFindResource("ActionChip") as Style,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(icon), Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.8,
            Width = 17, Height = 17, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = System.Windows.Media.Brushes.White });
        b.Content = sp;
        b.Click += (_, _) => onClick();
        AttachHint(b, tip);
        return b;
    }

    // ---- Sahne (içerik piksel) → ekran (Canvas DIP) dönüşümü ----
    private double SceneScaleX => _scene == null || _scene.Width <= 0 ? 1 : _selDip.Width / _scene.Width;
    private double SceneScaleY => _scene == null || _scene.Height <= 0 ? 1 : _selDip.Height / _scene.Height;

    private WpfRect SceneRectToScreen(SKRect r) => new(
        _selDip.X + r.Left * SceneScaleX, _selDip.Y + r.Top * SceneScaleY,
        r.Width * SceneScaleX, r.Height * SceneScaleY);

    // ---- Panel konumlandırma ----
    // ===================== Hover etiket =====================
    private void SetHoverHint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            HoverLabel.Visibility = Visibility.Collapsed;
        }
        else
        {
            HoverText.Text = text;
            HoverLabel.Visibility = Visibility.Visible;
            HoverLabel.UpdateLayout();
            Canvas.SetLeft(HoverLabel, ActualWidth - HoverLabel.ActualWidth - 12);
            Canvas.SetTop(HoverLabel, 12);
        }
    }

    /// <summary>Bir WPF kontrolüne hover etiket bağlar.</summary>
    private void AttachHint(FrameworkElement el, string hint)
    {
        el.MouseEnter += (_, _) => SetHoverHint(hint);
        el.MouseLeave += (_, _) => SetHoverHint(null);
        el.ToolTip = null; // system tooltip'i kapat
    }

    private void SyncPlaceholder()
    {
        bool show = _mode == CaptureMode.Free && _scene != null && _scene.Items.Count == 0;
        PlaceholderPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            PlaceholderPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double pw = PlaceholderPanel.DesiredSize.Width;
            double ph = PlaceholderPanel.DesiredSize.Height;
            Canvas.SetLeft(PlaceholderPanel, _selDip.X + (_selDip.Width - pw) / 2);
            Canvas.SetTop(PlaceholderPanel, _selDip.Y + (_selDip.Height - ph) / 2);
        }
    }

    // Araç çubuğunu görünür alan içinde tutar.
    private void ClampToolbar(ref double x, ref double y)
    {
        const double gap = 6;
        double w = Toolbar.ActualWidth, h = Toolbar.ActualHeight;
        double maxX = Math.Max(gap, ActualWidth - w - gap);
        double maxY = Math.Max(gap, ActualHeight - h - gap);
        x = Math.Clamp(x, gap, maxX);
        y = Math.Clamp(y, gap, maxY);
    }

    private void PositionPanels()
    {
        const double gap = 10;
        bool free = _mode == CaptureMode.Free;

        double tbW = Toolbar.ActualWidth, tbH = Toolbar.ActualHeight;
        if (tbW <= 0 || tbH <= 0) { Toolbar.UpdateLayout(); tbW = Toolbar.ActualWidth; tbH = Toolbar.ActualHeight; }
        if (ActionBar.ActualWidth <= 0) ActionBar.UpdateLayout();
        bool hasOpt = OptionBar.Visibility == Visibility.Visible;

        // Üst aksiyon çubuğu: üst-orta (modlar gizliyken).
        Canvas.SetLeft(ActionBar, Math.Round((ActualWidth - ActionBar.ActualWidth) / 2));
        Canvas.SetTop(ActionBar, 18);

        // Araç çubuğu konumu
        bool fullLike = free || _mode == CaptureMode.FullScreen;
        if (fullLike)
        {
            double tbX, tbY;
            if (_toolbarMoved)
            {
                // Kullanıcının sürüklediği konumu koru (ekrana yeniden sığdır)
                tbX = _toolbarPos.X; tbY = _toolbarPos.Y;
            }
            else
            {
                tbX = Math.Round((ActualWidth - tbW) / 2);
                tbY = ActualHeight - tbH - 24;
            }
            ClampToolbar(ref tbX, ref tbY);
            Canvas.SetLeft(Toolbar, tbX);
            Canvas.SetTop(Toolbar, tbY);
        }
        else
        {
            double tbX = _selDip.Right + gap;
            if (tbX + tbW > ActualWidth) tbX = _selDip.X - gap - tbW;
            if (tbX < 0) tbX = Math.Max(gap, ActualWidth - tbW - gap);
            double tbY = _selDip.Bottom - tbH;
            if (tbY < gap) tbY = _selDip.Y;
            if (tbY + tbH > ActualHeight) tbY = ActualHeight - tbH - gap;
            if (tbY < gap) tbY = gap;
            Canvas.SetLeft(Toolbar, tbX);
            Canvas.SetTop(Toolbar, tbY);
        }

        if (hasOpt) PositionOptionBar();
    }

    /// <summary>Seçenek şeridini seçili öğenin (varsa) hemen altına; yoksa makul varsayılana konumlar.</summary>
    private void PositionOptionBar()
    {
        const double gap = 8;
        // UpdateLayout sadece boyut henüz ölçülmemişse zorla; sürükleme sırasında AtualWidth geçerli
        double obW = OptionBar.ActualWidth, obH = OptionBar.ActualHeight;
        if (obW <= 0 || obH <= 0) { OptionBar.UpdateLayout(); obW = OptionBar.ActualWidth; obH = OptionBar.ActualHeight; }

        // Çapa dikdörtgeni: seçili öğe varsa onun ekran kutusu; yoksa serbest=alt-orta, bölge=seçim.
        WpfRect anchor;
        if (_canvas?.SelectedItem is { } sel)
            anchor = SceneRectToScreen(sel.Bounds);
        else if (_mode == CaptureMode.Free)
            anchor = new WpfRect((ActualWidth - obW) / 2, ActualHeight - 120, obW, 0);
        else
            anchor = _selDip;

        double obX = anchor.Left + (anchor.Width - obW) / 2;   // öğe altında ortalı
        double obY = anchor.Bottom + gap;

        // Ekrana sığdır
        if (obX < gap) obX = gap;
        if (obX + obW > ActualWidth - gap) obX = ActualWidth - obW - gap;
        if (obY + obH > ActualHeight - gap) obY = anchor.Top - gap - obH;  // alta sığmazsa üste
        if (obY < gap) obY = gap;

        // Araç çubuğuyla çakışırsa sola kaydır
        var tbRect = new WpfRect(Canvas.GetLeft(Toolbar), Canvas.GetTop(Toolbar), Toolbar.ActualWidth, Toolbar.ActualHeight);
        var obRect = new WpfRect(obX, obY, obW, obH);
        if (tbRect.IntersectsWith(obRect))
        {
            double alt = tbRect.Left - gap - obW;
            if (alt >= gap) obX = alt;
        }

        Canvas.SetLeft(OptionBar, obX);
        Canvas.SetTop(OptionBar, obY);
    }

    // ===================== Serbest mod: resim yapıştırma =====================
    private void TryPasteImage()
    {
        if (_scene == null) return;
        try
        {
            if (!Clipboard.ContainsImage()) return;
            var src = Clipboard.GetImage();
            if (src == null) return;
            var sk = BitmapSourceToSk(src);
            if (sk == null) return;

            float maxW = _scene.Width * 0.6f, maxH = _scene.Height * 0.6f;
            float scale = Math.Min(1f, Math.Min(maxW / sk.Width, maxH / sk.Height));
            float w = sk.Width * scale, h = sk.Height * scale;
            float cx = _scene.Width / 2f, cy = _scene.Height / 2f;
            int n = _scene.Items.OfType<ImageItem>().Count();
            float off = n * 30f;
            var item = new ImageItem { Bitmap = sk, Bounds = new SKRect(cx - w / 2 + off, cy - h / 2 + off, cx + w / 2 + off, cy + h / 2 + off) };
            _scene.Apply(new AddItemAction(item));
            _canvas?.SetSelection(item);
        }
        catch { }
    }

    private static SKBitmap? BitmapSourceToSk(System.Windows.Media.Imaging.BitmapSource src)
    {
        try
        {
            var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var buf = new byte[stride * h];
            conv.CopyPixels(buf, stride, 0);
            var sk = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, sk.GetPixels(), buf.Length);
            return sk;
        }
        catch { return null; }
    }

    // ===================== Metin düzenleme (in-place WYSIWYG) =====================
    private TextBox? _textBox;
    private TextItem? _editingItem;
    private string _editBeforeText = "";
    private bool _editCommitted;

    private void OnTextEditRequested(TextItem item)
    {
        if (_scene == null) return;
        _textEditing = true;
        _editingItem = item;
        _editBeforeText = item.Text;
        _editCommitted = false;
        item.Measure();

        double scaleY = SceneScaleY;
        // WYSIWYG: gerçek metni TextItem render eder (ribbon/gölge/renk). TextBox üstte saydam metinli,
        // yalnız caret+seçim görünür; yazdıkça item.Text güncellenir → kutu/ribbon büyür.
        var box = new TextBox
        {
            Text = item.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 8,
            FontFamily = new System.Windows.Media.FontFamily(item.FontFamily),
            FontSize = Math.Max(8, item.FontSize * scaleY),
            FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.Transparent,   // metin item'dan görünür
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CaretBrush = System.Windows.Media.Brushes.White,
            SelectionBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0x2F, 0x6F, 0xED)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _textBox = box;

        PlaceTextBox();
        System.Windows.Controls.Panel.SetZIndex(box, 1000);
        Root.Children.Add(box);

        // Canlı güncelleme: yazdıkça item metni → ribbon/punto WYSIWYG büyür; kutu yeniden konumlanır.
        box.TextChanged += (_, _) =>
        {
            if (_editingItem == null) return;
            _editingItem.Text = box.Text;
            _editingItem.Measure();
            _scene?.RaiseChanged();
            PlaceTextBox();
        };

        box.LostFocus += (_, _) => CommitTextEdit();
        box.PreviewKeyDown += (_, e) =>
        {
            // Enter = yeni satır; Ctrl+Enter = bitir. (Esc Window PreviewKeyDown'da.)
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            { e.Handled = true; CommitTextEdit(); }
        };

        box.Focus();
        box.SelectAll();
    }

    /// <summary>TextBox'ı düzenlenen item'ın ekran konum/boyutuna hizalar (metin başına).</summary>
    private void PlaceTextBox()
    {
        if (_textBox == null || _editingItem == null) return;
        double sx = _selDip.X + _editingItem.Position.X * SceneScaleX;
        double sy = _selDip.Y + _editingItem.Position.Y * SceneScaleY;
        Canvas.SetLeft(_textBox, sx);
        Canvas.SetTop(_textBox, sy);
    }

    private void CommitTextEdit()
    {
        if (_editCommitted || _editingItem == null || _scene == null) return;
        _editCommitted = true;
        var item = _editingItem;
        var box = _textBox;

        item.Text = box?.Text ?? item.Text;
        if (string.IsNullOrWhiteSpace(item.Text))
        {
            _scene.Apply(new RemoveItemAction(item));
        }
        else if (item.Text != _editBeforeText)
        {
            var after = (TextItem)item.Clone();
            var before = (TextItem)item.Clone();
            before.Text = _editBeforeText;
            _scene.Apply(new ModifyItemAction(item, before, after));
        }

        if (box != null) Root.Children.Remove(box);
        _textBox = null;
        _editingItem = null;
        _textEditing = false;
        _scene.RaiseChanged();
        _canvas?.Focus();
    }

    // ===================== Aksiyonlar =====================
    private void DoClearScene()
    {
        if (_scene == null) return;
        _scene.Items.Clear();
        _scene.ClearHistory();
        _scene.ResetStepCounter();
        _canvas?.ClearSelection();
        _scene.RaiseChanged();
    }

    private bool? AskTransparentBackground()
    {
        if (_mode != CaptureMode.Free) return false;
        var dlg = new BackgroundChoiceDialog { Owner = this };
        dlg.ShowDialog();
        return dlg.Result;
    }

    private SKBitmap RenderWithBackground(bool transparent)
    {
        // Crop seçimi devam ediyorsa export öncesi otomatik commit et
        if (_canvas != null && _canvas.IsSceneCropping)
            _canvas.CommitSceneCrop();

        var savedBg = _scene!.BackgroundColor;
        if (transparent)
            _scene.BackgroundColor = SkiaSharp.SKColors.Transparent;
        else if (savedBg.Alpha == 0)
            _scene.BackgroundColor = new SkiaSharp.SKColor(0x1F, 0x24, 0x30);
        var bmp = SceneRenderer.RenderToBitmap(_scene);
        _scene.BackgroundColor = savedBg;
        return bmp;
    }

    private void DoCopy()
    {
        if (_scene == null) return;
        try
        {
            var choice = AskTransparentBackground();
            if (choice == null) return;

            using var bmp = RenderWithBackground(choice == true);
            ImageExporter.CopyToClipboard(bmp);
            if (_mode == CaptureMode.Free) DoClearScene();
            Close();
        }
        catch (Exception ex) { MessageBox.Show("Kopyalama başarısız: " + ex.Message); }
    }

    private void DoSave()
    {
        if (_scene == null) return;
        try
        {
            var choice = AskTransparentBackground();
            if (choice == null) return;
            bool transparent = choice == true;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"ScreenForge_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = transparent ? "PNG|*.png" : "PNG|*.png|JPEG|*.jpg|WebP|*.webp",
                FilterIndex = transparent ? 1 : _settings.OutputFormat switch { ImageFormat.Jpeg => 2, ImageFormat.Webp => 3, _ => 1 },
                InitialDirectory = _settings.SaveDirectory,
            };
            if (dlg.ShowDialog() == true)
            {
                var fmt = transparent ? ImageFormat.Png
                    : dlg.FilterIndex switch { 2 => ImageFormat.Jpeg, 3 => ImageFormat.Webp, _ => ImageFormat.Png };

                using var bmp = RenderWithBackground(transparent);
                using var data = ImageExporter.Encode(bmp, fmt, _settings.Quality);
                using var fs = System.IO.File.OpenWrite(dlg.FileName);
                data.SaveTo(fs);
                if (_mode == CaptureMode.Free) DoClearScene();
                Close();
            }
        }
        catch (Exception ex) { MessageBox.Show("Kaydetme başarısız: " + ex.Message); }
    }

    private async void DoUpload()
    {
        if (_scene == null) return;

        var choice = AskTransparentBackground();
        if (choice == null) return;
        bool transparent = choice == true;

        byte[] bytes;
        string mime;
        try
        {
            using var bmp = RenderWithBackground(transparent);
            using var data = ImageExporter.Encode(bmp, ImageFormat.Png, 100);
            bytes = data.ToArray();
            mime = "image/png";
        }
        catch (Exception ex) { MessageBox.Show("Yükleme başarısız: " + ex.Message, "ScreenForge", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        if (_mode == CaptureMode.Free) DoClearScene();
        Close();
        try
        {
            var toast = new UploadToastWindow();
            toast.Show();
            IUploadProvider provider = new PrntscrUploadProvider();
            var result = await provider.UploadAsync(bytes, mime, toast.ReportProgress);
            toast.ShowResult(result.Url);
            if (_settings.AutoCopyLinkAfterUpload) { Clipboard.SetText(result.Url); toast.SetCopied(); }
            if (_settings.AutoCloseUploadWindow) toast.AutoCloseSoon();
        }
        catch (Exception ex) { MessageBox.Show("Yükleme başarısız: " + ex.Message, "ScreenForge", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // ===================== Klavye =====================
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // Inline metin düzenleme açıkken: kısayolları TextBox'a bırak (Esc PreviewKeyDown'da ele alınır).
        if (_textEditing) return;

        if (_phase != Phase.Edit) return;

        if (ctrl && e.Key == Key.Z) { if (shift) _scene?.Redo(); else _scene?.Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { _scene?.Redo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.C) { DoCopy(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.S) { DoSave(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.V) { TryPasteImage(); e.Handled = true; return; }
        if (e.Key is Key.Delete or Key.Back) { _canvas?.DeleteSelected(); e.Handled = true; return; }

        // Yön tuşlarıyla seçili öğeleri kaydır. Ctrl = 1px ince ayar, normal = 10px.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && _canvas?.SelectedItem != null)
        {
            float step = ctrl ? 1f : shift ? 30f : 10f;
            float dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0f;
            float dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0f;
            _canvas.NudgeSelection(dx, dy);
            e.Handled = true;
            return;
        }

        // Serbest modda C tuşu: sahne kırpma
        if (_mode == CaptureMode.Free && e.Key == Key.C && !ctrl)
        {
            _canvas?.BeginSceneCrop();
            e.Handled = true;
            return;
        }

        if (_canvas == null) return;
        EditorTool? t = e.Key switch
        {
            Key.V => EditorTool.Select, Key.R => EditorTool.Rectangle, Key.O => EditorTool.Ellipse,
            Key.A => EditorTool.Arrow, Key.L => EditorTool.Line, Key.P => EditorTool.Pen,
            Key.H => EditorTool.Highlight, Key.T => EditorTool.Text, Key.S => EditorTool.Step,
            Key.B => EditorTool.Blur, _ => null,
        };
        if (t.HasValue) { _canvas.Tool = t.Value; e.Handled = true; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings.Save();
        base.OnClosed(e);
    }
}
