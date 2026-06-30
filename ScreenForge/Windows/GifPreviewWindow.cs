using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenForge.Gif;
using DrawingBitmap       = System.Drawing.Bitmap;
using DrawingRect         = System.Drawing.Rectangle;
using DrawingPixelFormat  = System.Drawing.Imaging.PixelFormat;
using DrawingImageLockMode= System.Drawing.Imaging.ImageLockMode;
using WpfFontFamily       = System.Windows.Media.FontFamily;
using WpfImage            = System.Windows.Controls.Image;

namespace ScreenForge.Windows;

/// <summary>
/// GIF kayıt bittikten sonra açılan önizleme + düzenleme penceresi.
/// </summary>
public sealed class GifPreviewWindow
{
    private readonly GifRecorder _recorder;
    private List<byte[]> _frames;
    private readonly List<(int frameIndex, string key)> _keyEvents;
    private int _selectedIndex;

    // UI refs
    private StackPanel?      _thumbnailPanel;
    private ScrollViewer?    _thumbScroll;
    private WpfImage?        _previewImage;
    private Canvas?          _keyOverlayCanvas;
    private TextBlock?       _fpsLabel;
    private Slider?          _fpsSlider;
    private ComboBox?        _qualityCombo;
    private TextBox?         _widthBox;
    private TextBox?         _heightBox;
    private CheckBox?        _keepAspect;
    private TextBlock?       _statusLabel;
    private TextBlock?       _frameCountLabel;
    private System.Windows.Controls.ProgressBar? _progressBar;

    // Playback
    private DispatcherTimer? _playTimer;
    private bool             _playing;

    public GifPreviewWindow(GifRecorder recorder)
    {
        _recorder  = recorder;
        _frames    = recorder.Frames.Select(f => f.ToArray()).ToList();
        _keyEvents = recorder.KeyEvents.ToList();
    }

    public void Show()
    {
        var win = BuildWindow();
        win.Show();
        if (_frames.Count > 0) SelectFrame(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PENCERE YAPISI
    // ═══════════════════════════════════════════════════════════════════════════

    private Window BuildWindow()
    {
        // ── Title bar ─────────────────────────────────────────────────────────
        var titleText = new TextBlock
        {
            Text              = "GIF Önizleme",
            Foreground        = Brushes.White,
            FontSize          = 12,
            FontFamily        = new WpfFontFamily("Segoe UI"),
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(12, 0, 0, 0),
        };
        _frameCountLabel = new TextBlock
        {
            Text              = $"  {_frames.Count} kare",
            Foreground        = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            FontSize          = 11,
            FontFamily        = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var titleLeft = new StackPanel { Orientation = Orientation.Horizontal };
        titleLeft.Children.Add(titleText);
        titleLeft.Children.Add(_frameCountLabel);

        var closeBtn = MakeTitleBtn("✕");

        var titleBar = new Grid
        {
            Height     = 34,
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x1F)),
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleLeft, 0);
        Grid.SetColumn(closeBtn,  1);
        titleBar.Children.Add(titleLeft);
        titleBar.Children.Add(closeBtn);

        // ── Thumbnail şeridi ──────────────────────────────────────────────────
        _thumbnailPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4, 4, 4, 4),
        };

        // ScrollViewer — scrollbar gizli, mouse wheel + drag ile scroll
        _thumbScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Background                    = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x1F)),
            Height                        = 90,
            Content                       = _thumbnailPanel,
        };
        // Mouse wheel yatay scroll
        _thumbScroll.PreviewMouseWheel += (_, e) =>
        {
            _thumbScroll.ScrollToHorizontalOffset(_thumbScroll.HorizontalOffset - e.Delta * 0.5);
            e.Handled = true;
        };

        // Play/Pause/Stop satırı + şerit ayırıcı
        var playbackBar = BuildPlaybackBar();

        var stripPanel = new StackPanel();
        stripPanel.Children.Add(_thumbScroll);
        stripPanel.Children.Add(playbackBar);

        // ── Önizleme (sol) ────────────────────────────────────────────────────
        _previewImage = new WpfImage
        {
            Stretch           = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _keyOverlayCanvas = new Canvas { IsHitTestVisible = false };

        // Dashed border — ekranda da böyle görünüyor
        var dashedBorder = new System.Windows.Shapes.Rectangle
        {
            Stroke          = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill            = Brushes.Transparent,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        var previewContainer = new Grid { Margin = new Thickness(10) };
        previewContainer.Children.Add(dashedBorder);
        previewContainer.Children.Add(_previewImage);
        previewContainer.Children.Add(_keyOverlayCanvas);

        // ── Ayarlar paneli (sağ) ──────────────────────────────────────────────
        var settingsPanel = BuildSettingsPanel();

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) }); // ayırıcı
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        var divider = new Border { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x3A)) };
        Grid.SetColumn(previewContainer, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(settingsPanel, 2);
        contentGrid.Children.Add(previewContainer);
        contentGrid.Children.Add(divider);
        contentGrid.Children.Add(settingsPanel);

        // ── Status bar + progress ─────────────────────────────────────────────
        _statusLabel = new TextBlock
        {
            Text       = "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x68, 0x88)),
            FontSize   = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Margin     = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _progressBar = new System.Windows.Controls.ProgressBar
        {
            Minimum   = 0,
            Maximum   = 100,
            Value     = 0,
            Height    = 3,
            Visibility= Visibility.Collapsed,
            Foreground= new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
            Background= new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x3A)),
            BorderThickness = new Thickness(0),
        };
        var statusInner = new Grid { Height = 24 };
        statusInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        statusInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_statusLabel,  0);
        Grid.SetRow(_progressBar,  1);
        statusInner.Children.Add(_statusLabel);
        statusInner.Children.Add(_progressBar);
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x1C)),
            Child      = statusInner,
        };

        // ── Ana grid ──────────────────────────────────────────────────────────
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x28)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar,    0);
        Grid.SetRow(stripPanel,  1);
        Grid.SetRow(contentGrid, 2);
        Grid.SetRow(statusBar,   3);
        root.Children.Add(titleBar);
        root.Children.Add(stripPanel);
        root.Children.Add(contentGrid);
        root.Children.Add(statusBar);

        var win = new Window
        {
            Title          = "GIF Önizleme",
            WindowStyle    = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode     = ResizeMode.CanResize,
            ShowInTaskbar  = true,
            Width          = 960,
            Height         = 600,
            MinWidth       = 720,
            MinHeight      = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content        = root,
        };

        titleBar.MouseLeftButtonDown += (_, _) => win.DragMove();
        closeBtn.Click += (_, _) => { StopPlayback(); win.Close(); };
        win.Closed     += (_, _) => StopPlayback();

        win.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: StopPlayback(); win.Close(); break;
                case Key.Left:   if (_selectedIndex > 0)               SelectFrame(_selectedIndex - 1); break;
                case Key.Right:  if (_selectedIndex < _frames.Count-1) SelectFrame(_selectedIndex + 1); break;
                case Key.Delete: DeleteCurrentFrame(); break;
                case Key.Space:  TogglePlayback(); break;
            }
        };

        win.Loaded += (_, _) => RefreshThumbnails();
        return win;
    }

    // ─── Playback bar ─────────────────────────────────────────────────────────

    private StackPanel BuildPlaybackBar()
    {
        var playBtn  = MakeIconBtn("▶");
        var pauseBtn = MakeIconBtn("⏸");
        var stopBtn2 = MakeIconBtn("⏹");

        playBtn.Click  += (_, _) => StartPlayback();
        pauseBtn.Click += (_, _) => PausePlayback();
        stopBtn2.Click += (_, _) => StopPlayback();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background  = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x1F)),
            Height      = 28,
        };
        bar.Children.Add(playBtn);
        bar.Children.Add(pauseBtn);
        bar.Children.Add(stopBtn2);
        return bar;
    }

    private void StartPlayback()
    {
        if (_frames.Count == 0) return;
        _playing = true;
        if (_playTimer == null)
        {
            _playTimer = new DispatcherTimer();
            _playTimer.Tick += (_, _) =>
            {
                if (!_playing) { _playTimer.Stop(); return; }
                int next = (_selectedIndex + 1) % _frames.Count;
                SelectFrame(next);
            };
        }
        int fps = _fpsSlider != null ? Math.Max(1, (int)_fpsSlider.Value) : _recorder.Fps;
        _playTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        _playTimer.Start();
    }

    private void PausePlayback()
    {
        _playing = false;
        _playTimer?.Stop();
    }

    private void StopPlayback()
    {
        _playing = false;
        _playTimer?.Stop();
        if (_frames.Count > 0) SelectFrame(0);
    }

    private void TogglePlayback()
    {
        if (_playing) PausePlayback();
        else StartPlayback();
    }

    // ─── Ayarlar paneli ───────────────────────────────────────────────────────

    private StackPanel BuildSettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };

        // FPS
        panel.Children.Add(MakeLabel("FPS"));
        _fpsSlider = new Slider
        {
            Minimum          = 1,
            Maximum          = 30,
            Value            = _recorder.Fps,
            TickFrequency    = 1,
            IsSnapToTickEnabled = true,
            Margin           = new Thickness(0, 4, 0, 0),
        };
        _fpsLabel = new TextBlock
        {
            Text       = $"{_recorder.Fps} fps",
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Margin     = new Thickness(0, 2, 0, 10),
        };
        _fpsSlider.ValueChanged += (_, e) =>
        {
            int v = (int)e.NewValue;
            _fpsLabel!.Text = $"{v} fps";
            if (_playing)
            {
                _playTimer!.Interval = TimeSpan.FromMilliseconds(1000.0 / v);
            }
        };
        panel.Children.Add(_fpsSlider);
        panel.Children.Add(_fpsLabel);

        // Renk kalitesi
        panel.Children.Add(MakeLabel("Renk Kalitesi"));
        _qualityCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 10) };
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "256 Renk (Yüksek)", Tag = 256 });
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "128 Renk (Orta)",   Tag = 128 });
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "64 Renk (Düşük)",   Tag = 64  });
        _qualityCombo.SelectedIndex = 0;
        panel.Children.Add(_qualityCombo);

        // Çıktı boyutu
        panel.Children.Add(MakeLabel("Çıktı Boyutu"));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        _widthBox  = MakeNumBox(_recorder.Width.ToString());
        _heightBox = MakeNumBox(_recorder.Height.ToString());
        sizeRow.Children.Add(_widthBox);
        sizeRow.Children.Add(new TextBlock { Text = " × ", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x68, 0x88)), VerticalAlignment = VerticalAlignment.Center });
        sizeRow.Children.Add(_heightBox);
        panel.Children.Add(sizeRow);

        _keepAspect = new CheckBox
        {
            IsChecked = true,
            Margin    = new Thickness(0, 0, 0, 12),
            Content   = new TextBlock { Text = "Oranı koru", Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)), FontSize = 11 },
        };
        panel.Children.Add(_keepAspect);

        double ar = _recorder.Height > 0 ? (double)_recorder.Width / _recorder.Height : 1.0;
        bool _updatingSize = false;
        _widthBox.TextChanged += (_, _) =>
        {
            if (_updatingSize) return;
            if (_keepAspect.IsChecked == true && int.TryParse(_widthBox.Text, out int w) && w > 0)
            {
                int h = (int)Math.Round(w / ar);
                if (_heightBox!.Text != h.ToString()) { _updatingSize = true; _heightBox.Text = h.ToString(); _updatingSize = false; }
            }
        };
        _heightBox.TextChanged += (_, _) =>
        {
            if (_updatingSize) return;
            if (_keepAspect.IsChecked == true && int.TryParse(_heightBox.Text, out int h) && h > 0)
            {
                int w = (int)Math.Round(h * ar);
                if (_widthBox!.Text != w.ToString()) { _updatingSize = true; _widthBox.Text = w.ToString(); _updatingSize = false; }
            }
        };

        panel.Children.Add(MakeSep());

        // Tekrarlananları sil
        var dupBtn = MakeBtn("Tekrarlananları Sil", Color.FromRgb(0x2A, 0x35, 0x4D));
        dupBtn.Click += (_, _) => RemoveDuplicates();
        panel.Children.Add(dupBtn);

        panel.Children.Add(MakeSep());

        // Kare Sil / Çoğalt
        var frameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var delBtn = MakeBtn("Kare Sil", Color.FromRgb(0x6B, 0x25, 0x25));
        delBtn.Width  = 100;
        delBtn.Margin = new Thickness(0, 0, 6, 4);
        delBtn.Click += (_, _) => DeleteCurrentFrame();
        var dupF = MakeBtn("Çoğalt", Color.FromRgb(0x2A, 0x35, 0x4D));
        dupF.Width = 100;
        dupF.Click += (_, _) => DuplicateCurrentFrame();
        frameRow.Children.Add(delBtn);
        frameRow.Children.Add(dupF);
        panel.Children.Add(frameRow);

        panel.Children.Add(MakeSep());

        var saveBtn = MakeBtn("GIF Kaydet", Color.FromRgb(0xEA, 0x6F, 0x12));
        saveBtn.FontWeight = FontWeights.SemiBold;
        saveBtn.FontSize   = 13;
        saveBtn.Height     = 36;
        saveBtn.Click += async (_, _) => await SaveGifAsync();
        panel.Children.Add(saveBtn);

        return panel;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FRAME İŞLEMLERİ
    // ═══════════════════════════════════════════════════════════════════════════

    private void SelectFrame(int index)
    {
        if (index < 0 || index >= _frames.Count) return;
        _selectedIndex = index;
        UpdatePreview();
        UpdateThumbnailHighlight();
        ScrollThumbnailIntoView(index);
        _statusLabel!.Text = $"Kare {index + 1} / {_frames.Count}";
    }

    private void DeleteCurrentFrame()
    {
        if (_frames.Count <= 1 || _selectedIndex >= _frames.Count) return;
        _frames.RemoveAt(_selectedIndex);
        if (_selectedIndex >= _frames.Count) _selectedIndex = _frames.Count - 1;
        RefreshThumbnails();
        SelectFrame(_selectedIndex);
    }

    private void DuplicateCurrentFrame()
    {
        if (_selectedIndex >= _frames.Count) return;
        _frames.Insert(_selectedIndex + 1, _frames[_selectedIndex].ToArray());
        RefreshThumbnails();
        SelectFrame(_selectedIndex + 1);
    }

    /// <summary>
    /// Ardışık benzer kareleri siler.
    /// SequenceEqual yerine piksel-bazlı eşiksiz (exact int karşılaştırma, ScreenToGif mantığı).
    /// GDI BGRA layout → 4 byte = 1 int olarak karşılaştır — çok hızlı.
    /// </summary>
    private void RemoveDuplicates()
    {
        if (_frames.Count < 2) { _statusLabel!.Text = "0 tekrarlanan kare silindi"; return; }

        var result  = new List<byte[]> { _frames[0] };
        int removed = 0;

        for (int i = 1; i < _frames.Count; i++)
        {
            if (!AreFramesIdentical(_frames[i], _frames[i - 1]))
                result.Add(_frames[i]);
            else
                removed++;
        }

        _frames = result;
        if (_selectedIndex >= _frames.Count) _selectedIndex = _frames.Count - 1;
        RefreshThumbnails();
        SelectFrame(_selectedIndex);
        _statusLabel!.Text = $"{removed} tekrarlanan kare silindi";
    }

    /// <summary>
    /// 4-byte aligned int karşılaştırma — SequenceEqual'dan ~4× hızlı.
    /// </summary>
    private static bool AreFramesIdentical(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        // int span karşılaştırması (4 byte = 1 piksel)
        var sa = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(a.AsSpan());
        var sb = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(b.AsSpan());
        return sa.SequenceEqual(sb);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  THUMBNAIL
    // ═══════════════════════════════════════════════════════════════════════════

    private void RefreshThumbnails()
    {
        _thumbnailPanel!.Children.Clear();
        _frameCountLabel!.Text = $"  {_frames.Count} kare";
        for (int i = 0; i < _frames.Count; i++)
        {
            int idx   = i;
            var thumb = BuildThumb(idx);
            thumb.MouseLeftButtonDown += (_, _) => SelectFrame(idx);
            _thumbnailPanel.Children.Add(thumb);
        }
        UpdateThumbnailHighlight();
    }

    private Border BuildThumb(int index)
    {
        var src = ToBitmapSource(_frames[index], _recorder.Width, _recorder.Height);
        var img = new WpfImage { Source = src, Width = 64, Height = 48, Stretch = Stretch.Uniform };

        var num = new TextBlock
        {
            Text       = (index + 1).ToString(),
            FontSize   = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x68, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin     = new Thickness(0, 1, 0, 0),
        };
        var inner = new StackPanel();
        inner.Children.Add(img);
        inner.Children.Add(num);

        return new Border
        {
            Width           = 72,
            Height          = 70,
            Margin          = new Thickness(2, 2, 2, 2),
            CornerRadius    = new CornerRadius(3),
            BorderThickness = new Thickness(2),
            BorderBrush     = Brushes.Transparent,
            Background      = new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x30)),
            Child           = inner,
            Cursor          = Cursors.Hand,
        };
    }

    private void UpdateThumbnailHighlight()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12));
        int i = 0;
        foreach (UIElement el in _thumbnailPanel!.Children)
        {
            if (el is Border b)
                b.BorderBrush = i == _selectedIndex ? accent : Brushes.Transparent;
            i++;
        }
    }

    private void ScrollThumbnailIntoView(int index)
    {
        if (_thumbScroll == null || _thumbnailPanel == null) return;
        double thumbW = 76; // width + margin
        double offset = index * thumbW;
        double visible = _thumbScroll.ViewportWidth;
        double cur     = _thumbScroll.HorizontalOffset;
        if (offset < cur)
            _thumbScroll.ScrollToHorizontalOffset(offset);
        else if (offset + thumbW > cur + visible)
            _thumbScroll.ScrollToHorizontalOffset(offset + thumbW - visible);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ÖNİZLEME
    // ═══════════════════════════════════════════════════════════════════════════

    private void UpdatePreview()
    {
        if (_previewImage == null || _selectedIndex >= _frames.Count) return;
        _previewImage.Source = ToBitmapSource(_frames[_selectedIndex], _recorder.Width, _recorder.Height);
        UpdateKeyOverlay();
    }

    private void UpdateKeyOverlay()
    {
        _keyOverlayCanvas!.Children.Clear();
        var keys = _keyEvents.Where(e => e.frameIndex == _selectedIndex).Select(e => e.key).ToList();
        if (keys.Count == 0) return;

        var badge = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(8, 4, 8, 4),
            Child         = new TextBlock { Text = string.Join(" + ", keys), Foreground = Brushes.White, FontSize = 12, FontFamily = new WpfFontFamily("Segoe UI") },
        };
        Canvas.SetBottom(badge, 12);
        Canvas.SetLeft(badge, 12);
        _keyOverlayCanvas.Children.Add(badge);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KAYDET
    // ═══════════════════════════════════════════════════════════════════════════

    private async System.Threading.Tasks.Task SaveGifAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter     = "Animasyonlu GIF|*.gif",
            DefaultExt = ".gif",
            FileName   = "kayit",
        };
        if (dlg.ShowDialog() != true) return;

        int fps    = _fpsSlider != null ? Math.Max(1, (int)_fpsSlider.Value) : _recorder.Fps;
        int colors = GetColorCount();
        int outW   = ParseOrDefault(_widthBox?.Text,  _recorder.Width);
        int outH   = ParseOrDefault(_heightBox?.Text, _recorder.Height);
        bool resize= outW != _recorder.Width || outH != _recorder.Height;

        PausePlayback();
        _statusLabel!.Text    = resize ? "Yeniden boyutlandırılıyor..." : "Kaydediliyor...";
        _progressBar!.Value   = 0;
        _progressBar.Visibility = Visibility.Visible;

        // Resize CPU-yoğun — Task.Run içinde yap
        List<byte[]> toSave;
        if (resize)
        {
            int srcW = _recorder.Width, srcH = _recorder.Height;
            toSave = await System.Threading.Tasks.Task.Run(() =>
                _frames.Select(f => ResizeFrame(f, srcW, srcH, outW, outH)).ToList());
            _statusLabel!.Text = "Kaydediliyor...";
            _progressBar.Value = 0;
        }
        else
        {
            toSave = _frames;
        }

        await _recorder.SaveAsync(
            dlg.FileName,
            fpsOverride    : fps,
            colorCount     : colors,
            framesOverride : toSave,
            widthOverride  : outW,
            heightOverride : outH,
            progress       : p => Application.Current?.Dispatcher.Invoke(() =>
            {
                _progressBar!.Value = p * 100;
                _statusLabel!.Text  = $"Kaydediliyor... {(int)(p * 100)}%";
            }));

        _progressBar.Visibility = Visibility.Collapsed;
        _statusLabel!.Text = $"Kaydedildi → {Path.GetFileName(dlg.FileName)}";
    }

    private int GetColorCount()
    {
        if (_qualityCombo?.SelectedItem is ComboBoxItem ci && ci.Tag is int v) return v;
        return 256;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ═══════════════════════════════════════════════════════════════════════════

    private static byte[] ResizeFrame(byte[] bgra, int srcW, int srcH, int dstW, int dstH)
    {
        using var src  = new DrawingBitmap(srcW, srcH, DrawingPixelFormat.Format32bppArgb);
        var sd = src.LockBits(new DrawingRect(0, 0, srcW, srcH), DrawingImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(bgra, 0, sd.Scan0, bgra.Length);
        src.UnlockBits(sd);

        using var dst  = new DrawingBitmap(dstW, dstH);
        using var g    = System.Drawing.Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, dstW, dstH);

        var result = new byte[dstW * dstH * 4];
        var dd = dst.LockBits(new DrawingRect(0, 0, dstW, dstH), DrawingImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(dd.Scan0, result, 0, result.Length);
        dst.UnlockBits(dd);
        for (int i = 3; i < result.Length; i += 4) result[i] = 255;
        return result;
    }

    private static BitmapSource ToBitmapSource(byte[] bgra, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        b.Freeze();
        return b;
    }

    private static int ParseOrDefault(string? s, int def)
        => int.TryParse(s, out int v) && v > 0 ? v : def;

    private static TextBlock MakeLabel(string text) => new()
    {
        Text       = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
        FontSize   = 10,
        FontFamily = new WpfFontFamily("Segoe UI"),
        Margin     = new Thickness(0, 6, 0, 0),
    };

    private static Border MakeSep() => new()
    {
        Height     = 1,
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x3A)),
        Margin     = new Thickness(0, 8, 0, 8),
    };

    private static Button MakeBtn(string text, Color bg) => new()
    {
        Content         = text,
        Height          = 30,
        Background      = new SolidColorBrush(bg),
        Foreground      = Brushes.White,
        BorderThickness = new Thickness(0),
        FontSize        = 11,
        FontFamily      = new WpfFontFamily("Segoe UI"),
        Cursor          = Cursors.Hand,
        Margin          = new Thickness(0, 0, 0, 4),
    };

    private static Button MakeIconBtn(string icon) => new()
    {
        Content         = icon,
        Width           = 30, Height = 24,
        Background      = Brushes.Transparent,
        Foreground      = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
        BorderThickness = new Thickness(0),
        FontSize        = 13,
        Cursor          = Cursors.Hand,
        Margin          = new Thickness(2, 2, 2, 2),
        VerticalContentAlignment   = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private static TextBox MakeNumBox(string val) => new()
    {
        Text            = val,
        Width           = 65,
        Height          = 24,
        Background      = new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x30)),
        Foreground      = Brushes.White,
        BorderBrush     = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x4D)),
        BorderThickness = new Thickness(1),
        Padding         = new Thickness(4, 2, 4, 2),
        FontFamily      = new WpfFontFamily("Segoe UI"),
        FontSize        = 11,
    };

    private static Button MakeTitleBtn(string content) => new()
    {
        Content         = content,
        Width           = 34, Height = 34,
        Background      = Brushes.Transparent,
        Foreground      = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
        BorderThickness = new Thickness(0),
        FontSize        = 13,
        Cursor          = Cursors.Hand,
    };
}
