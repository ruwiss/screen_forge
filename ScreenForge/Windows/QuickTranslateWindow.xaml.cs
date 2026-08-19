using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;
using ScreenForge.Settings;
using ScreenForge.Translate;

namespace ScreenForge.Windows;

public partial class QuickTranslateWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly AppSettings _settings;
    private readonly GoogleTextTranslateClient _client = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _copyReset;
    private CancellationTokenSource? _cts;
    private int _requestId;
    private string _lastCopied = "";
    private bool _closing;

    /// <summary>Pencere gerçekten odağı alana kadar dışarı-tık kapatması beklemede kalır.</summary>
    private bool _canAutoClose;

    public QuickTranslateWindow(AppSettings settings, string? seedText = null)
    {
        _settings = settings;
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = TranslateNowAsync();
        };

        _copyReset = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _copyReset.Tick += (_, _) =>
        {
            _copyReset.Stop();
            ShowCopyGlyph(copied: false);
        };

        // Metin _debounce kurulduktan sonra yazılmalı; TextChanged zamanlayıcıya dokunuyor.
        if (!string.IsNullOrWhiteSpace(seedText))
            TxtInput.Text = seedText;

        Loaded += (_, _) =>
        {
            PlaceOnCursorMonitor();
            ForceForeground();
            TxtInput.Focus();
            TxtInput.CaretIndex = TxtInput.Text.Length;
        };
        Activated += (_, _) => Dispatcher.BeginInvoke(
            () => _canAutoClose = true, DispatcherPriority.ApplicationIdle);
        Deactivated += OnDeactivated;
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _debounce.Stop();
            _copyReset.Stop();
            _cts?.Cancel();
        };
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closing || !_canAutoClose || !IsVisible)
            return;
        Close();
    }

    /// <summary>
    /// Başka bir uygulama önplandayken açıldığımızda odağı güvenle alır.
    /// </summary>
    /// <remarks>
    /// Windows önplan kilidi yüzünden düz <c>Activate()</c> sessizce başarısız olur;
    /// pencere arkada kalır ve kullanıcıya hiç açılmamış gibi görünür.
    /// </remarks>
    private void ForceForeground()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            uint foreignThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint ownThread = GetCurrentThreadId();
            bool attached = foreignThread != 0 && foreignThread != ownThread
                && AttachThreadInput(ownThread, foreignThread, true);

            Activate();
            SetForegroundWindow(hwnd);

            if (attached)
                AttachThreadInput(ownThread, foreignThread, false);
        }
        catch
        {
            Activate();
        }
    }

    public void FocusExisting()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ForceForeground();
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    /// <summary>Kısayolla yakalanan seçili metni kutuya yazar ve çeviriyi başlatır.</summary>
    public void UseIncomingText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            TxtInput.Text = text;
            TxtInput.CaretIndex = TxtInput.Text.Length;
        }

        FocusExisting();
    }

    private void PlaceOnCursorMonitor()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        var dpi = VisualTreeHelper.GetDpi(this);
        double left = screen.WorkingArea.Left / dpi.DpiScaleX;
        double top = screen.WorkingArea.Top / dpi.DpiScaleY;
        double width = screen.WorkingArea.Width / dpi.DpiScaleX;
        double height = screen.WorkingArea.Height / dpi.DpiScaleY;

        MaxHeight = Math.Max(140, height * 0.55);
        ResultScroll.MaxHeight = Math.Max(72, height * 0.32);

        Left = left + (width - ActualWidth) / 2;
        Top = top + (height - ActualHeight) / 2;
    }

    private const double CardRadius = 16;

    private void OnRootCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RootCard.ActualWidth < 1 || RootCard.ActualHeight < 1)
            return;

        RootCard.Clip = new RectangleGeometry(
            new Rect(0, 0, RootCard.ActualWidth, RootCard.ActualHeight),
            CardRadius, CardRadius);
    }

    private void OnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
            Close();
    }

    private void OnChromeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        DragMove();
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control
            && TxtResult.IsKeyboardFocusWithin)
        {
            CopyResult();
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        TxtPlaceholder.Visibility = string.IsNullOrWhiteSpace(TxtInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _debounce?.Stop();
        _cts?.Cancel();
        ShowCopyGlyph(copied: false);

        if (string.IsNullOrWhiteSpace(TxtInput.Text))
        {
            ResultHost.Visibility = Visibility.Collapsed;
            TxtResult.Text = "";
            TxtStatus.Text = "";
            return;
        }

        _debounce?.Start();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyResult();

    private void CopyResult()
    {
        string text = TxtResult.Text;
        if (string.IsNullOrEmpty(text))
            return;
        try
        {
            Clipboard.SetText(text);
            _lastCopied = text;
            TxtStatus.Text = "";
            ShowCopyGlyph(copied: true);
            _copyReset.Stop();
            _copyReset.Start();
        }
        catch
        {
            TxtStatus.Text = "Kopyalanamadı";
        }
    }

    /// <summary>Kopyalama geri bildirimi metin yerine ikonla verilir.</summary>
    private void ShowCopyGlyph(bool copied)
    {
        if (BtnCopy?.Content is not ShapePath glyph)
            return;

        if (TryFindResource(copied ? "IconCheck" : "IconCopy") is Geometry data)
            glyph.Data = data;

        glyph.Stroke = copied && TryFindResource("AccentBrush") is Brush accent
            ? accent
            : new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF2));
        BtnCopy.ToolTip = copied ? "Kopyalandı" : "Çeviriyi kopyala";
    }

    private async Task TranslateNowAsync()
    {
        string text = TxtInput.Text.Trim();
        if (text.Length == 0)
            return;

        string native = string.IsNullOrWhiteSpace(_settings.TranslateNativeLanguage)
            ? "en" : _settings.TranslateNativeLanguage.Trim();
        string pair = string.IsNullOrWhiteSpace(_settings.TranslatePairLanguage)
            ? TranslateLanguageDefaults.DefaultPair(native)
            : _settings.TranslatePairLanguage.Trim();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int id = ++_requestId;

        ResultHost.Visibility = Visibility.Visible;
        TxtStatus.Text = "Çevriliyor…";
        BtnCopy.IsEnabled = false;

        try
        {
            var first = await _client.TranslateAsync(text, native, ct).ConfigureAwait(true);
            if (id != _requestId || ct.IsCancellationRequested || !IsVisible)
                return;

            if (first == null)
            {
                TxtResult.Text = "";
                TxtStatus.Text = "Çeviri başarısız";
                return;
            }

            TranslateResult shown = first.Value;
            if (TranslateLanguageRouter.ShouldTranslateToPair(native, first.Value.SourceLang, text, first.Value.Text)
                && !string.Equals(pair, native, StringComparison.OrdinalIgnoreCase))
            {
                var second = await _client.TranslateAsync(text, pair, ct).ConfigureAwait(true);
                if (id != _requestId || ct.IsCancellationRequested || !IsVisible)
                    return;
                if (second != null)
                    shown = second.Value;
            }

            TxtResult.Text = shown.Text;
            TxtStatus.Text = "";
            ShowCopyGlyph(copied: shown.Text == _lastCopied);
            BtnCopy.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // Yeni yazım / kapanış.
        }
        catch
        {
            if (id != _requestId)
                return;
            TxtResult.Text = "";
            TxtStatus.Text = "Çeviri başarısız";
            BtnCopy.IsEnabled = false;
        }
    }
}
