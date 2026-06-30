using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenForge.Gif;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRect = System.Drawing.Rectangle;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;

namespace ScreenForge.Windows;

/// <summary>
/// GIF kayıt bittikten sonra açılan önizleme penceresi.
/// Kare düzenleme, FPS/kalite/boyut ayarları ve kaydetme içerir.
/// </summary>
public sealed class GifPreviewWindow
{
    private readonly GifRecorder _recorder;
    private List<byte[]> _frames;
    private readonly List<(int frameIndex, string key)> _keyEvents;
    private int _selectedIndex;

    // UI bileşenleri
    private StackPanel? _thumbnailPanel;
    private WpfImage? _previewImage;
    private Canvas? _keyOverlayCanvas;
    private Slider? _fpsSlider;
    private TextBlock? _fpsLabel;
    private ComboBox? _qualityCombo;
    private TextBox? _widthBox;
    private TextBox? _heightBox;
    private CheckBox? _keepAspect;
    private TextBlock? _statusLabel;

    public GifPreviewWindow(GifRecorder recorder)
    {
        _recorder = recorder;
        _frames = recorder.Frames.Select(f => f.ToArray()).ToList();
        _keyEvents = recorder.KeyEvents.ToList();
    }

    public void Show()
    {
        var win = BuildWindow();
        win.Show();
        if (_frames.Count > 0) SelectFrame(0);
    }

    private Window BuildWindow()
    {
        // ─── Başlık çubuğu ───────────────────────────────────────────────────
        var titleLabel = new TextBlock
        {
            Text = "GIF Önizleme",
            Foreground = Brushes.White,
            FontSize = 12,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };

        var frameCountLabel = new TextBlock
        {
            Text = $"{_frames.Count} kare",
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 32, Height = 30,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(titleLabel);
        titleStack.Children.Add(frameCountLabel);

        var titleGrid = new Grid { Height = 32, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x24)) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(closeBtn, 1);
        titleGrid.Children.Add(titleStack);
        titleGrid.Children.Add(closeBtn);

        // ─── Thumbnail şeridi ─────────────────────────────────────────────────
        _thumbnailPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x24)),
            Margin = new Thickness(4, 4, 4, 0),
        };

        var thumbScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x24)),
            Height = 68,
            Content = _thumbnailPanel,
        };

        // ─── Büyük önizleme + key overlay ────────────────────────────────────
        _previewImage = new WpfImage
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _keyOverlayCanvas = new Canvas
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var previewGrid = new Grid();
        previewGrid.Children.Add(_previewImage);
        previewGrid.Children.Add(_keyOverlayCanvas);

        // ─── Ayarlar paneli ───────────────────────────────────────────────────
        var settingsPanel = BuildSettingsPanel();

        // ─── Ana grid ─────────────────────────────────────────────────────────
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        Grid.SetColumn(previewGrid, 0);
        Grid.SetColumn(settingsPanel, 1);
        contentGrid.Children.Add(previewGrid);
        contentGrid.Children.Add(settingsPanel);

        var mainGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x2D)) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // title
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // thumbnails
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status
        Grid.SetRow(titleGrid, 0);
        Grid.SetRow(thumbScroll, 1);
        Grid.SetRow(contentGrid, 2);
        mainGrid.Children.Add(titleGrid);
        mainGrid.Children.Add(thumbScroll);
        mainGrid.Children.Add(contentGrid);

        // Status bar
        _statusLabel = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Margin = new Thickness(8, 3, 8, 3),
        };
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x24)),
            Child = _statusLabel,
        };
        Grid.SetRow(statusBar, 3);
        mainGrid.Children.Add(statusBar);

        var win = new Window
        {
            Title = "GIF Önizleme",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = true,
            Width = 900,
            Height = 580,
            MinWidth = 700,
            MinHeight = 450,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = mainGrid,
        };

        // Sürükleme
        titleGrid.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) win.DragMove(); };

        closeBtn.Click += (_, _) => win.Close();

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
            if (e.Key == Key.Left && _selectedIndex > 0) SelectFrame(_selectedIndex - 1);
            if (e.Key == Key.Right && _selectedIndex < _frames.Count - 1) SelectFrame(_selectedIndex + 1);
            if (e.Key == Key.Delete) DeleteCurrentFrame();
        };

        win.Loaded += (_, _) => RefreshThumbnails();

        return win;
    }

    private Border BuildSettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };

        // FPS
        panel.Children.Add(MakeLabel("FPS"));
        _fpsSlider = new Slider
        {
            Minimum = 1, Maximum = 30,
            Value = _recorder.Fps,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _fpsLabel = new TextBlock
        {
            Text = $"{_recorder.Fps} fps",
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Margin = new Thickness(0, 2, 0, 8),
        };
        _fpsSlider.ValueChanged += (_, e) => _fpsLabel!.Text = $"{(int)e.NewValue} fps";
        panel.Children.Add(_fpsSlider);
        panel.Children.Add(_fpsLabel);

        // Kalite
        panel.Children.Add(MakeLabel("Renk Kalitesi"));
        _qualityCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 8) };
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "256 Renk (Yüksek)", Tag = 256 });
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "128 Renk (Orta)", Tag = 128 });
        _qualityCombo.Items.Add(new ComboBoxItem { Content = "64 Renk (Düşük)", Tag = 64 });
        _qualityCombo.SelectedIndex = 0;
        panel.Children.Add(_qualityCombo);

        // Boyut
        panel.Children.Add(MakeLabel("Çıktı Boyutu"));
        var sizeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        _widthBox = new TextBox { Text = _recorder.Width.ToString(), Width = 60, Margin = new Thickness(0, 0, 4, 0) };
        _heightBox = new TextBox { Text = _recorder.Height.ToString(), Width = 60 };
        var xLabel = new TextBlock { Text = "×", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        sizeStack.Children.Add(_widthBox);
        sizeStack.Children.Add(xLabel);
        sizeStack.Children.Add(_heightBox);
        panel.Children.Add(sizeStack);

        _keepAspect = new CheckBox
        {
            Content = new TextBlock { Text = "Oranı koru", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)), FontSize = 11 },
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(_keepAspect);

        // Oran takibi
        double aspectRatio = _recorder.Height > 0 ? (double)_recorder.Width / _recorder.Height : 1.0;
        _widthBox.TextChanged += (_, _) =>
        {
            if (_keepAspect?.IsChecked == true && int.TryParse(_widthBox.Text, out int w) && w > 0)
            {
                int h = (int)Math.Round(w / aspectRatio);
                if (_heightBox!.Text != h.ToString()) _heightBox.Text = h.ToString();
            }
        };
        _heightBox.TextChanged += (_, _) =>
        {
            if (_keepAspect?.IsChecked == true && int.TryParse(_heightBox.Text, out int h) && h > 0)
            {
                int w = (int)Math.Round(h * aspectRatio);
                if (_widthBox!.Text != w.ToString()) _widthBox.Text = w.ToString();
            }
        };

        // Ayırıcı
        panel.Children.Add(MakeSeparator());

        // Tekrarlananları sil
        var removeDupBtn = MakeButton("Tekrarlananları Sil", Color.FromRgb(0x2A, 0x35, 0x4D));
        removeDupBtn.Click += (_, _) => RemoveDuplicates();
        panel.Children.Add(removeDupBtn);

        panel.Children.Add(MakeSeparator());

        // Kare işlemleri
        var frameOpsStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var deleteBtn = MakeButton("Kare Sil", Color.FromRgb(0x6B, 0x25, 0x25));
        deleteBtn.Width = 90;
        deleteBtn.Margin = new Thickness(0, 0, 4, 0);
        deleteBtn.Click += (_, _) => DeleteCurrentFrame();
        var dupeBtn = MakeButton("Çoğalt", Color.FromRgb(0x2A, 0x35, 0x4D));
        dupeBtn.Width = 90;
        dupeBtn.Click += (_, _) => DuplicateCurrentFrame();
        frameOpsStack.Children.Add(deleteBtn);
        frameOpsStack.Children.Add(dupeBtn);
        panel.Children.Add(frameOpsStack);

        panel.Children.Add(MakeSeparator());

        // Kaydet butonu
        var saveBtn = MakeButton("GIF Kaydet", Color.FromRgb(0xEA, 0x6F, 0x12));
        saveBtn.FontWeight = FontWeights.SemiBold;
        saveBtn.Click += async (_, _) => await SaveGifAsync();
        panel.Children.Add(saveBtn);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x4D)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
            Padding = new Thickness(0),
        };
        return container;
    }

    // ─── Frame işlemleri ──────────────────────────────────────────────────────

    private void SelectFrame(int index)
    {
        if (index < 0 || index >= _frames.Count) return;
        _selectedIndex = index;
        UpdatePreview();
        UpdateThumbnailHighlight();
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
        var copy = _frames[_selectedIndex].ToArray();
        _frames.Insert(_selectedIndex + 1, copy);
        RefreshThumbnails();
        SelectFrame(_selectedIndex + 1);
    }

    private void RemoveDuplicates()
    {
        if (_frames.Count < 2) return;
        var result = new List<byte[]> { _frames[0] };
        for (int i = 1; i < _frames.Count; i++)
            if (!_frames[i].SequenceEqual(_frames[i - 1]))
                result.Add(_frames[i]);
        int removed = _frames.Count - result.Count;
        _frames = result;
        if (_selectedIndex >= _frames.Count) _selectedIndex = _frames.Count - 1;
        RefreshThumbnails();
        SelectFrame(_selectedIndex);
        _statusLabel!.Text = $"{removed} tekrarlanan kare silindi";
    }

    // ─── Thumbnail ────────────────────────────────────────────────────────────

    private void RefreshThumbnails()
    {
        if (_thumbnailPanel == null) return;
        _thumbnailPanel.Children.Clear();
        for (int i = 0; i < _frames.Count; i++)
        {
            int idx = i;
            var thumb = BuildThumbnail(idx);
            thumb.MouseLeftButtonDown += (_, _) => SelectFrame(idx);
            _thumbnailPanel.Children.Add(thumb);
        }
        UpdateThumbnailHighlight();
    }

    private Border BuildThumbnail(int index)
    {
        var src = ToBitmapSource(_frames[index], _recorder.Width, _recorder.Height);
        var img = new WpfImage
        {
            Source = src,
            Width = 60, Height = 45,
            Stretch = Stretch.Uniform,
        };

        var numLabel = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(numLabel);

        var border = new Border
        {
            Width = 68, Height = 66,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x30)),
            Child = stack,
            Cursor = Cursors.Hand,
            Tag = index,
        };
        return border;
    }

    private void UpdateThumbnailHighlight()
    {
        if (_thumbnailPanel == null) return;
        var accent = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12));
        for (int i = 0; i < _thumbnailPanel.Children.Count; i++)
        {
            if (_thumbnailPanel.Children[i] is Border b)
                b.BorderBrush = i == _selectedIndex ? accent : Brushes.Transparent;
        }
    }

    // ─── Önizleme ─────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        if (_previewImage == null || _selectedIndex >= _frames.Count) return;
        _previewImage.Source = ToBitmapSource(_frames[_selectedIndex], _recorder.Width, _recorder.Height);
        UpdateKeyOverlay();
    }

    private void UpdateKeyOverlay()
    {
        if (_keyOverlayCanvas == null) return;
        _keyOverlayCanvas.Children.Clear();

        var keysOnFrame = _keyEvents
            .Where(e => e.frameIndex == _selectedIndex)
            .Select(e => e.key)
            .ToList();

        if (keysOnFrame.Count == 0) return;

        var text = string.Join(" + ", keysOnFrame);
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontFamily = new WpfFontFamily("Segoe UI"),
            },
        };
        Canvas.SetBottom(badge, 10);
        Canvas.SetLeft(badge, 10);
        _keyOverlayCanvas.Children.Add(badge);
    }

    // ─── GIF Kaydet ───────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task SaveGifAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Animasyonlu GIF|*.gif",
            DefaultExt = ".gif",
            FileName = "kayit",
        };
        if (dlg.ShowDialog() != true) return;

        int fps = _fpsSlider != null ? (int)_fpsSlider.Value : _recorder.Fps;
        int colorCount = GetSelectedColorCount();
        int outW = ParseOrDefault(_widthBox?.Text, _recorder.Width);
        int outH = ParseOrDefault(_heightBox?.Text, _recorder.Height);
        bool needResize = outW != _recorder.Width || outH != _recorder.Height;

        _statusLabel!.Text = "Kaydediliyor...";

        var framesToSave = needResize
            ? _frames.Select(f => ResizeFrame(f, _recorder.Width, _recorder.Height, outW, outH)).ToList()
            : _frames;

        await _recorder.SaveAsync(
            dlg.FileName,
            fpsOverride: fps,
            colorCount: colorCount,
            framesOverride: framesToSave,
            progress: p => Application.Current?.Dispatcher.Invoke(() =>
                _statusLabel!.Text = $"Kaydediliyor... %{(int)(p * 100)}"));

        _statusLabel!.Text = $"Kaydedildi: {Path.GetFileName(dlg.FileName)}";
    }

    private int GetSelectedColorCount()
    {
        if (_qualityCombo?.SelectedItem is ComboBoxItem item && item.Tag is int v) return v;
        return 256;
    }

    // ─── Frame resize ─────────────────────────────────────────────────────────

    private static byte[] ResizeFrame(byte[] bgra, int srcW, int srcH, int dstW, int dstH)
    {
        using var srcBmp = new DrawingBitmap(srcW, srcH, DrawingPixelFormat.Format32bppArgb);
        var bmpData = srcBmp.LockBits(new DrawingRect(0, 0, srcW, srcH), DrawingImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(bgra, 0, bmpData.Scan0, bgra.Length);
        srcBmp.UnlockBits(bmpData);

        using var dstBmp = new DrawingBitmap(dstW, dstH);
        using var g = System.Drawing.Graphics.FromImage(dstBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(srcBmp, 0, 0, dstW, dstH);

        var result = new byte[dstW * dstH * 4];
        var dstData = dstBmp.LockBits(new DrawingRect(0, 0, dstW, dstH), DrawingImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        Marshal.Copy(dstData.Scan0, result, 0, result.Length);
        dstBmp.UnlockBits(dstData);
        for (int i = 3; i < result.Length; i += 4) result[i] = 255;
        return result;
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static BitmapSource ToBitmapSource(byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private static int ParseOrDefault(string? text, int fallback)
        => int.TryParse(text, out int v) && v > 0 ? v : fallback;

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
        FontSize = 10,
        FontFamily = new WpfFontFamily("Segoe UI"),
        Margin = new Thickness(0, 6, 0, 2),
    };

    private static Border MakeSeparator() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x4D)),
        Margin = new Thickness(0, 8, 0, 8),
    };

    private static Button MakeButton(string text, Color bg) => new()
    {
        Content = text,
        Height = 30,
        Background = new SolidColorBrush(bg),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        FontSize = 11,
        FontFamily = new WpfFontFamily("Segoe UI"),
        Cursor = Cursors.Hand,
        Margin = new Thickness(0, 0, 0, 4),
    };
}
