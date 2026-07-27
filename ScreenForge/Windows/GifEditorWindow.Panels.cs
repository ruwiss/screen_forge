using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScreenForge.Gif;
using ScreenForge.Gif.Editing;
using ScreenForge.Gif.Encoder;
using ScreenForge.Gif.Input;
using ScreenForge.Settings;
using DrawingColor = System.Drawing.Color;
using SfMouseButtons = ScreenForge.Gif.Input.MouseButtons;
using WpfImage = System.Windows.Controls.Image;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

public sealed partial class GifEditorWindow
{
    // ─── Kare paneli denetimleri ──────────────────────────────────────────────
    private TextBox? _delayBox;
    private ComboBox? _delayModeCombo;
    private Slider? _fpsSlider;
    private TextBlock? _fpsLabel;
    private Slider? _scaleDelaySlider;
    private TextBlock? _scaleDelayLabel;
    private Slider? _reduceKeepSlider;
    private TextBlock? _reduceKeepLabel;
    private Slider? _reduceDropSlider;
    private TextBlock? _reduceDropLabel;
    private Slider? _similaritySlider;
    private TextBlock? _similarityLabel;
    private CheckBox? _keepInputFramesCheck;
    private Slider? _loopSimilaritySlider;
    private TextBlock? _loopSimilarityLabel;

    // ─── Dışa aktarma denetimleri ─────────────────────────────────────────────
    private ComboBox? _colorCombo;
    private ComboBox? _quantizerCombo;
    private Slider? _samplingSlider;
    private TextBlock? _samplingLabel;
    private CheckBox? _ditheringCheck;
    private CheckBox? _globalPaletteCheck;
    private CheckBox? _optimizeCheck;
    private Slider? _toleranceSlider;
    private TextBlock? _toleranceLabel;
    private TextBox? _widthBox;
    private TextBox? _heightBox;
    private CheckBox? _keepAspectCheck;
    private Button? _cancelExportButton;

    // ─── Kaplama denetimleri ──────────────────────────────────────────────────
    private CheckBox? _clickHighlightCheck;
    private CheckBox? _cursorHighlightCheck;
    private CheckBox? _showKeysCheck;
    private Slider? _highlightRadiusSlider;
    private TextBlock? _highlightRadiusLabel;

    private CheckBox? _captionCheck;
    private TextBox? _captionBox;
    private ComboBox? _captionPlacement;
    private Slider? _captionSizeSlider;
    private TextBlock? _captionSizeLabel;

    private CheckBox? _progressCheck;
    private ComboBox? _progressStyleCombo;
    private ComboBox? _progressReadoutCombo;
    private ComboBox? _progressDecimalsCombo;
    private Grid? _progressDecimalsRow;
    private ComboBox? _progressPlacement;
    private Slider? _progressThicknessSlider;
    private TextBlock? _progressThicknessLabel;

    private CheckBox? _borderCheck;
    private Slider? _borderThicknessSlider;
    private TextBlock? _borderThicknessLabel;

    private CheckBox? _watermarkCheck;
    private TextBox? _watermarkBox;
    private ComboBox? _watermarkPlacement;
    private TextBlock? _watermarkFileLabel;
    private Slider? _watermarkScaleSlider;
    private string? _watermarkImagePath;

    // Kırpma durumu
    private bool _cropping;
    private Point _cropStart;
    private WpfRectangle? _cropRect;

    /// <summary>Boyut alanları programca yazılırken oran koruma geri çağırmasını susturur.</summary>
    private bool _suppressAspectSync;

    // ═══════════════════════════════════════════════════════════════════════════
    //  PANEL KURULUMU
    // ═══════════════════════════════════════════════════════════════════════════

    private void BuildPanels()
    {
        BuildExportPanel();
        BuildFramePanel();
        BuildOverlayPanel();
    }

    private void BuildExportPanel()
    {
        var gif = _settings?.Gif ?? new GifSettings();

        ExportPanel.Children.Add(Header("Boyut"));

        _widthBox = NumberBox(_document.Width.ToString());
        _heightBox = NumberBox(_document.Height.ToString());
        _keepAspectCheck = Check("Oranı koru", true);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        sizeRow.Children.Add(_widthBox);
        sizeRow.Children.Add(new TextBlock
        {
            Text = "×",
            Margin = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMutedBrush"),
        });
        sizeRow.Children.Add(_heightBox);
        ExportPanel.Children.Add(sizeRow);
        ExportPanel.Children.Add(_keepAspectCheck);

        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (int percent in new[] { 100, 75, 50, 25 })
        {
            int p = percent;
            var button = ToolButton($"%{percent}", $"Kaynağın %{percent} boyutu");
            button.Click += (_, _) => ApplyExportScale(p);
            scaleRow.Children.Add(button);
        }
        ExportPanel.Children.Add(scaleRow);

        WireAspectRatio();

        ExportPanel.Children.Add(Separator());
        ExportPanel.Children.Add(Header("Kalite"));

        _colorCombo = Combo(("256 renk (yüksek)", 256), ("128 renk (orta)", 128), ("64 renk (düşük)", 64));
        _colorCombo.SelectedIndex = gif.ColorCount switch { 128 => 1, 64 => 2, _ => 0 };
        _colorCombo.SelectionChanged += (_, _) => UpdateEstimate();
        ExportPanel.Children.Add(LabeledRow("Palet", _colorCombo));

        _optimizeCheck = Check("Değişmeyen alanı atla", gif.OptimizeUnchangedPixels,
            "Kareler arası aynı kalan pikselleri saydam yazar — en büyük boyut kazancı");
        _optimizeCheck.Checked += (_, _) => UpdateEstimate();
        _optimizeCheck.Unchecked += (_, _) => UpdateEstimate();
        ExportPanel.Children.Add(_optimizeCheck);

        // ─── Gelişmiş: varsayılan gizli ───────────────────────────────────────
        var advanced = new StackPanel();

        _quantizerCombo = Combo(("Neural (kaliteli)", QuantizerType.Neural), ("Octree (hızlı)", QuantizerType.Octree));
        _quantizerCombo.SelectedIndex = string.Equals(gif.Quantizer, "Octree", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        advanced.Children.Add(LabeledRow("Yöntem", _quantizerCombo));

        (_samplingSlider, _samplingLabel) = SliderRow(advanced, "Örnekleme", 1, 20, gif.SamplingFactor,
            "1 = en iyi kalite (yavaş), 20 = en hızlı");

        _quantizerCombo.SelectionChanged += (_, _) =>
        {
            bool neural = SelectedValue(_quantizerCombo, QuantizerType.Neural) == QuantizerType.Neural;
            _samplingSlider!.IsEnabled = neural;
            _samplingLabel!.Opacity = neural ? 1 : 0.4;
        };

        _ditheringCheck = Check("Dithering", gif.Dithering,
            "Gradyanlarda bantlaşmayı azaltır; kodlama yavaşlar, dosya büyür");
        advanced.Children.Add(_ditheringCheck);

        _globalPaletteCheck = Check("Global palet", gif.UseGlobalPalette,
            "Tüm kareler tek palet paylaşır; dosya küçülür");
        _globalPaletteCheck.Checked += (_, _) => UpdateEstimate();
        _globalPaletteCheck.Unchecked += (_, _) => UpdateEstimate();
        advanced.Children.Add(_globalPaletteCheck);

        (_toleranceSlider, _toleranceLabel) = SliderRow(advanced, "Tolerans", 0, 32, gif.ChangeTolerance,
            "Kanal başına izin verilen fark. Yüksek değer dosyayı küçültür, hafif iz bırakabilir");
        _toleranceSlider.ValueChanged += (_, _) => UpdateEstimate();

        ExportPanel.Children.Add(Collapsible("Gelişmiş", advanced));
        ExportPanel.Children.Add(Separator());

        _cancelExportButton = new Button
        {
            Content = "Dışa aktarmayı iptal et",
            Style = (Style)FindResource("EditorDangerButton"),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _cancelExportButton.Click += (_, _) => CancelExport();
        ExportPanel.Children.Add(_cancelExportButton);
    }

    private void BuildFramePanel()
    {
        FramePanel.Children.Add(Header("Süre"));

        _delayBox = NumberBox("100");
        _delayBox.ToolTip = "Seçili karelerin ekranda kalma süresi (ms)";

        var applyRow = new StackPanel { Orientation = Orientation.Horizontal };
        applyRow.Children.Add(_delayBox);

        var applySelected = ToolButton("Seçili", "Gecikmeyi seçili karelere uygula");
        applySelected.Click += (_, _) => ApplyDelay(allFrames: false);
        applyRow.Children.Add(applySelected);

        var applyAll = ToolButton("Tümü", "Gecikmeyi tüm karelere uygula");
        applyAll.Click += (_, _) => ApplyDelay(allFrames: true);
        applyRow.Children.Add(applyAll);

        FramePanel.Children.Add(applyRow);

        (_fpsSlider, _fpsLabel) = SliderRow(FramePanel, "FPS", 1, 50, _recorder.Fps,
            "Tüm karelere eşit süre verir");
        var applyFps = ToolButton("FPS uygula", "Tüm kareleri bu hıza ayarla");
        applyFps.Click += (_, _) =>
        {
            int fps = (int)_fpsSlider!.Value;
            Edit($"{fps} FPS", f => FrameOperations.SetFps(f, fps));
        };
        FramePanel.Children.Add(applyFps);

        (_scaleDelaySlider, _scaleDelayLabel) = SliderRow(FramePanel, "Hız", 10, 400, 100,
            "%50 iki kat hızlandırır, %200 yavaşlatır");
        var applyScale = ToolButton("Hızı uygula", "Seçili karelerin süresini ölçekle");
        applyScale.Click += (_, _) =>
        {
            double percent = _scaleDelaySlider!.Value;
            var selection = SelectedIndexes();
            Edit($"Hız %{percent:0}", f => FrameOperations.ScaleDelay(f, selection, percent));
        };
        FramePanel.Children.Add(applyScale);

        FramePanel.Children.Add(Separator());
        FramePanel.Children.Add(Header("Dosyayı küçült"));

        var dedupeButton = ToolButton("Tekrarlananları sil", "Ardışık aynı kareleri birleştir");
        dedupeButton.Click += (_, _) =>
        {
            double similarity = _similaritySlider?.Value ?? 100;
            bool keepInput = _keepInputFramesCheck?.IsChecked != false;
            Edit("Tekrarlananları sil",
                f => FrameOperations.RemoveDuplicates(f, similarity, DuplicateRemoval.Last,
                    CurrentDelayMode(), keepInput), changesFrameCount: true);
        };
        FramePanel.Children.Add(dedupeButton);

        var reduceButton = ToolButton("Kareleri azalt", "Düzenli aralıklarla kare atarak dosyayı küçült");
        reduceButton.Click += (_, _) =>
        {
            int keep = (int)(_reduceKeepSlider?.Value ?? 2);
            int drop = (int)(_reduceDropSlider?.Value ?? 1);
            Edit($"Azalt {keep}:{drop}", f => FrameOperations.Reduce(f, keep, drop, CurrentDelayMode()),
                changesFrameCount: true);
        };
        FramePanel.Children.Add(reduceButton);

        var loopButton = ToolButton("Döngüyü pürüzsüzleştir", "Başa dönüşü dikişsiz hâle getir");
        loopButton.Click += (_, _) =>
        {
            double similarity = _loopSimilaritySlider?.Value ?? 95;
            Edit("Pürüzsüz döngü", f => FrameOperations.SmoothLoop(f, similarity), changesFrameCount: true);
        };
        FramePanel.Children.Add(loopButton);

        // ─── Gelişmiş: varsayılan gizli ───────────────────────────────────────
        var advanced = new StackPanel();

        _delayModeCombo = Combo(
            ("Öncekine ekle", DelayMergeMode.AddToPrevious),
            ("Eşit dağıt", DelayMergeMode.Distribute),
            ("At (kısalt)", DelayMergeMode.Discard));
        _delayModeCombo.SelectedIndex = 0;
        advanced.Children.Add(LabeledRow("Silince", _delayModeCombo));

        (_similaritySlider, _similarityLabel) = SliderRow(advanced, "Benzerlik", 50, 100, 100,
            "Tekrarlanan sayılmak için gereken en düşük benzerlik. %100 yalnızca birebir aynı kareleri siler");

        _keepInputFramesCheck = Check("Tıklama/tuş taşıyanı koru", true,
            "Girdi vurgusu olan kareler görsel olarak aynı olsa da korunur");
        advanced.Children.Add(_keepInputFramesCheck);

        (_reduceKeepSlider, _reduceKeepLabel) = SliderRow(advanced, "Tut", 1, 10, 2,
            "Kare azaltmada kaç kare korunacak");
        (_reduceDropSlider, _reduceDropLabel) = SliderRow(advanced, "At", 1, 10, 1,
            "Ardından kaç kare atılacak");

        (_loopSimilaritySlider, _loopSimilarityLabel) = SliderRow(advanced, "Döngü", 50, 100, 95,
            "Pürüzsüz döngü için gereken benzerlik");

        FramePanel.Children.Add(Collapsible("Gelişmiş", advanced));
    }

    private void BuildOverlayPanel()
    {
        var gif = _settings?.Gif ?? new GifSettings();

        OverlayPanel.Children.Add(Header("Fare ve klavye"));

        _clickHighlightCheck = Check("Tıklama vurgusu", gif.HighlightClicks,
            "Tıklamayı renkli daire ile göster: sol sarı, sağ kırmızı, orta camgöbeği");
        _clickHighlightCheck.Checked += (_, _) => UpdatePreview();
        _clickHighlightCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_clickHighlightCheck);

        _cursorHighlightCheck = Check("İmleç vurgusu (sürekli)", gif.HighlightCursor,
            "Tıklama olmasa da imlecin etrafında sarı vurgu göster");
        _cursorHighlightCheck.Checked += (_, _) => UpdatePreview();
        _cursorHighlightCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_cursorHighlightCheck);

        (_highlightRadiusSlider, _highlightRadiusLabel) = SliderRow(OverlayPanel, "Yarıçap", 4, 48,
            (int)gif.HighlightRadius, "Vurgu dairesinin yarıçapı");
        _highlightRadiusSlider.ValueChanged += (_, _) => UpdatePreview();

        _showKeysCheck = Check("Tuşları göster", gif.ShowKeys,
            "Basılan klavye tuşlarını köşede rozet olarak göster");
        _showKeysCheck.Checked += (_, _) => UpdatePreview();
        _showKeysCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_showKeysCheck);

        OverlayPanel.Children.Add(Separator());
        OverlayPanel.Children.Add(Header("Altyazı"));

        _captionCheck = Check("Altyazı ekle", false);
        _captionCheck.Checked += (_, _) => UpdatePreview();
        _captionCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_captionCheck);

        _captionBox = new TextBox
        {
            Style = (Style)FindResource("EditorTextBox"),
            Margin = new Thickness(0, 3, 0, 3),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 44,
            AcceptsReturn = true,
        };
        _captionBox.TextChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_captionBox);

        _captionPlacement = PlacementCombo(OverlayPlacement.BottomCenter);
        _captionPlacement.SelectionChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(LabeledRow("Konum", _captionPlacement));

        (_captionSizeSlider, _captionSizeLabel) = SliderRow(OverlayPanel, "Boyut", 10, 72, 28, "Yazı boyutu");
        _captionSizeSlider.ValueChanged += (_, _) => UpdatePreview();

        OverlayPanel.Children.Add(Separator());
        OverlayPanel.Children.Add(Header("İlerleme göstergesi"));

        _progressCheck = Check("İlerleme ekle", false);
        _progressCheck.Checked += (_, _) => UpdatePreview();
        _progressCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_progressCheck);

        _progressStyleCombo = Combo(("Çubuk", ProgressStyle.Bar), ("Yazı", ProgressStyle.Text));
        _progressStyleCombo.SelectionChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(LabeledRow("Biçim", _progressStyleCombo));

        _progressReadoutCombo = Combo(
            ("Saniye", ProgressReadout.Seconds),
            ("Kare", ProgressReadout.Frames),
            ("Yüzde", ProgressReadout.Percent));
        _progressReadoutCombo.SelectionChanged += (_, _) =>
        {
            UpdateProgressOptionState();
            UpdatePreview();
        };
        OverlayPanel.Children.Add(LabeledRow("Gösterim", _progressReadoutCombo));

        // Saniye biçimi: tam sayı mı salise mi.
        _progressDecimalsCombo = Combo(("3.6 / 7.2 sn", 1), ("3 / 7 sn", 0));
        _progressDecimalsCombo.ToolTip = "Saniye gösteriminde ondalık basamak";
        _progressDecimalsCombo.SelectionChanged += (_, _) => UpdatePreview();
        _progressDecimalsRow = LabeledRow("Hassasiyet", _progressDecimalsCombo);
        OverlayPanel.Children.Add(_progressDecimalsRow);

        _progressPlacement = PlacementCombo(OverlayPlacement.BottomLeft);
        _progressPlacement.SelectionChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(LabeledRow("Konum", _progressPlacement));

        (_progressThicknessSlider, _progressThicknessLabel) = SliderRow(OverlayPanel, "Kalınlık", 2, 24, 6,
            "Çubuk kalınlığı");
        _progressThicknessSlider.ValueChanged += (_, _) => UpdatePreview();

        _progressStyleCombo.SelectionChanged += (_, _) => UpdateProgressOptionState();
        UpdateProgressOptionState();

        OverlayPanel.Children.Add(Separator());
        OverlayPanel.Children.Add(Header("Kenarlık ve filigran"));

        _borderCheck = Check("Kenarlık ekle", false);
        _borderCheck.Checked += (_, _) => UpdatePreview();
        _borderCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_borderCheck);

        (_borderThicknessSlider, _borderThicknessLabel) = SliderRow(OverlayPanel, "Kalınlık", 1, 20, 2,
            "Kenarlık içe doğru çizilir, kare boyutu değişmez");
        _borderThicknessSlider.ValueChanged += (_, _) => UpdatePreview();

        _watermarkCheck = Check("Filigran ekle", false);
        _watermarkCheck.Checked += (_, _) => UpdatePreview();
        _watermarkCheck.Unchecked += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_watermarkCheck);

        _watermarkBox = new TextBox
        {
            Style = (Style)FindResource("EditorTextBox"),
            Margin = new Thickness(0, 3, 0, 3),
        };
        _watermarkBox.TextChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(_watermarkBox);

        // Logo: seçilirse metin yerine görsel çizilir.
        var logoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

        var pickLogo = ToolButton("Logo seç…", "Filigran olarak bir görsel kullan");
        pickLogo.Click += (_, _) => PickWatermarkImage();
        logoRow.Children.Add(pickLogo);

        var clearLogo = ToolButton("Kaldır", "Logoyu kaldır, metne dön");
        clearLogo.Click += (_, _) =>
        {
            _watermarkImagePath = null;
            _watermarkFileLabel!.Text = "";
            UpdatePreview();
        };
        logoRow.Children.Add(clearLogo);

        OverlayPanel.Children.Add(logoRow);

        _watermarkFileLabel = new TextBlock
        {
            Style = (Style)FindResource("EditorFieldLabel"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 3),
        };
        OverlayPanel.Children.Add(_watermarkFileLabel);

        (_watermarkScaleSlider, _) = SliderRow(OverlayPanel, "Logo boyu", 3, 40, 12,
            "Logonun kare genişliğine oranı (%)");
        _watermarkScaleSlider.ValueChanged += (_, _) => UpdatePreview();

        _watermarkPlacement = PlacementCombo(OverlayPlacement.BottomRight);
        _watermarkPlacement.SelectionChanged += (_, _) => UpdatePreview();
        OverlayPanel.Children.Add(LabeledRow("Konum", _watermarkPlacement));
    }

    /// <summary>Girdi verisi yoksa ilgili anahtarları pasifleştirir.</summary>
    private void UpdatePanelState()
    {
        if (_clickHighlightCheck == null)
            return;

        bool hasCursor = _document.Frames.Any(f => f.Input.CursorVisible);
        bool hasKeys = _document.Frames.Any(f => f.Input.Keys.Count > 0);

        _clickHighlightCheck.IsEnabled = hasCursor;
        _cursorHighlightCheck!.IsEnabled = hasCursor;
        _highlightRadiusSlider!.IsEnabled = hasCursor;
        _showKeysCheck!.IsEnabled = hasKeys;

        if (!hasCursor)
            _clickHighlightCheck.ToolTip = "Bu kayıtta imleç verisi yok";
        if (!hasKeys)
            _showKeysCheck.ToolTip = "Bu kayıtta klavye verisi yok";
    }

    private DelayMergeMode CurrentDelayMode()
        => SelectedValue(_delayModeCombo, DelayMergeMode.AddToPrevious);

    private void ApplyDelay(bool allFrames)
    {
        if (_delayBox == null || !int.TryParse(_delayBox.Text, out int delay))
            return;

        delay = Math.Clamp(delay, 10, 60000);
        _delayBox.Text = delay.ToString();

        var target = allFrames ? Enumerable.Range(0, _document.FrameCount).ToList() : SelectedIndexes();
        Edit(allFrames ? $"Tüm kareler {delay} ms" : $"{delay} ms",
            f => FrameOperations.SetDelay(f, target, delay));
    }

    /// <summary>
    /// İlerleme ayarlarından yalnızca geçerli olanları gösterir.
    /// </summary>
    /// <remarks>
    /// Kalınlık yalnızca çubukta, hassasiyet yalnızca saniye gösteriminde
    /// anlamlıdır; ilgisiz denetimi göstermek paneli kalabalıklaştırır.
    /// </remarks>
    private void UpdateProgressOptionState()
    {
        if (_progressDecimalsRow == null)
            return;

        bool isText = SelectedValue(_progressStyleCombo, ProgressStyle.Bar) == ProgressStyle.Text;
        bool isSeconds = SelectedValue(_progressReadoutCombo, ProgressReadout.Seconds) == ProgressReadout.Seconds;

        _progressDecimalsRow.Visibility = isText && isSeconds ? Visibility.Visible : Visibility.Collapsed;

        if (_progressThicknessSlider != null)
            _progressThicknessSlider.IsEnabled = !isText;
    }

    /// <summary>Filigran için görsel seçtirir.</summary>
    private void PickWatermarkImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Filigran görseli seç",
            Filter = "Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm dosyalar|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        _watermarkImagePath = dialog.FileName;
        _watermarkFileLabel!.Text = System.IO.Path.GetFileName(dialog.FileName);

        // Logo seçmek filigranı da açar; ayrıca kutuyu işaretlemek gerekmesin.
        if (_watermarkCheck != null)
            _watermarkCheck.IsChecked = true;

        UpdatePreview();
        ShowToast("Filigran görseli seçildi");
    }

    private void ApplyExportScale(int percent)
    {
        _widthBox!.Text = Math.Max(1, _document.Width * percent / 100).ToString();
        _heightBox!.Text = Math.Max(1, _document.Height * percent / 100).ToString();
    }

    private void WireAspectRatio()
    {
        bool updating = false;

        _widthBox!.TextChanged += (_, _) =>
        {
            UpdateEstimate();
            if (updating || _suppressAspectSync || _keepAspectCheck!.IsChecked != true) return;
            if (!int.TryParse(_widthBox.Text, out int w) || w <= 0 || _document.Width <= 0) return;

            double ratio = (double)_document.Height / _document.Width;
            string h = Math.Max(1, (int)Math.Round(w * ratio)).ToString();
            if (_heightBox!.Text == h) return;

            updating = true;
            _heightBox.Text = h;
            updating = false;
        };

        _heightBox!.TextChanged += (_, _) =>
        {
            UpdateEstimate();
            if (updating || _suppressAspectSync || _keepAspectCheck!.IsChecked != true) return;
            if (!int.TryParse(_heightBox.Text, out int h) || h <= 0 || _document.Height <= 0) return;

            double ratio = (double)_document.Width / _document.Height;
            string w = Math.Max(1, (int)Math.Round(h * ratio)).ToString();
            if (_widthBox.Text == w) return;

            updating = true;
            _widthBox.Text = w;
            updating = false;
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KIRPMA
    // ═══════════════════════════════════════════════════════════════════════════

    private void BeginCrop()
    {
        if (_document.FrameCount == 0)
            return;

        StopPlayback();
        _cropping = true;
        CropCanvas.Visibility = Visibility.Visible;
        CropHint.Visibility = Visibility.Visible;
        CropCanvas.Children.Clear();
        _cropRect = null;
        CropHintText.Text = "Kırpılacak alanı sürükleyerek seçin";
        SetStatus("Kırpma: alanı sürükleyin, sonra Uygula");
    }

    private void CancelCrop()
    {
        _cropping = false;
        CropCanvas.Visibility = Visibility.Collapsed;
        CropHint.Visibility = Visibility.Collapsed;
        CropCanvas.Children.Clear();
        _cropRect = null;
    }

    private void OnCropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_cropping)
            return;

        _cropStart = ClampToCanvas(e.GetPosition(CropCanvas));
        CropCanvas.Children.Clear();

        _cropRect = new WpfRectangle
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(48, 234, 111, 18)),
        };

        Canvas.SetLeft(_cropRect, _cropStart.X);
        Canvas.SetTop(_cropRect, _cropStart.Y);
        CropCanvas.Children.Add(_cropRect);
        CropCanvas.CaptureMouse();
    }

    private void OnCropMouseMove(object sender, MouseEventArgs e)
    {
        if (!_cropping || _cropRect == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = ClampToCanvas(e.GetPosition(CropCanvas));

        double x = Math.Min(current.X, _cropStart.X);
        double y = Math.Min(current.Y, _cropStart.Y);
        double w = Math.Abs(current.X - _cropStart.X);
        double h = Math.Abs(current.Y - _cropStart.Y);

        Canvas.SetLeft(_cropRect, x);
        Canvas.SetTop(_cropRect, y);
        _cropRect.Width = w;
        _cropRect.Height = h;

        // Kaynak piksel cinsinden boyutu göster.
        CropHintText.Text = $"Seçim: {w / _zoom:0} × {h / _zoom:0} px";
    }

    private void OnCropMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_cropping)
            return;

        CropCanvas.ReleaseMouseCapture();

        var rect = CurrentCropRect();
        CropHintText.Text = rect == null
            ? "Kırpılacak alanı sürükleyerek seçin"
            : $"Seçim: {rect.Value.Width} × {rect.Value.Height} px";
    }

    /// <summary>Fare konumunu görüntü sınırları içinde tutar.</summary>
    private Point ClampToCanvas(Point point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, CropCanvas.ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, CropCanvas.ActualHeight)));

    private async Task ApplyCropAsync()
    {
        var rect = CurrentCropRect();
        if (rect is not { Width: >= 2, Height: >= 2 })
        {
            SetStatus("Önce sürükleyerek bir alan seçin");
            return;
        }

        int sourceWidth = _document.Width, sourceHeight = _document.Height;
        var area = rect.Value;
        CancelCrop();

        await EditPixelsAsync("Kırp",
            f => ImageOperations.Crop(f, sourceWidth, sourceHeight, area), area.Width, area.Height);

        // Çıktı boyutu artık kırpılmış kareyi izlesin; oran geri çağırması
        // araya girip yüksekliği ezmesin diye ikisi birlikte yazılır.
        SetExportSize(area.Width, area.Height);
        ZoomToFit();
    }

    /// <summary>Seçim dikdörtgenini kaynak piksel koordinatına çevirir.</summary>
    private Int32Rect? CurrentCropRect()
    {
        if (_cropRect == null || _cropRect.Width < 1 || _cropRect.Height < 1)
            return null;

        double left = Canvas.GetLeft(_cropRect);
        double top = Canvas.GetTop(_cropRect);

        if (double.IsNaN(left) || double.IsNaN(top))
            return null;

        // Tuval tam olarak görüntü boyutunda olduğu için ölçek yalnızca zoom'dur.
        return ImageOperations.ScreenRectToSource(left, top, _cropRect.Width, _cropRect.Height,
            _zoom, _document.Width, _document.Height);
    }

    /// <summary>
    /// Dışa aktarma boyutunu tek seferde yazar.
    /// Oran koruma geri çağırması araya girmesin diye geçici olarak kapatılır.
    /// </summary>
    private void SetExportSize(int width, int height)
    {
        if (_widthBox == null || _heightBox == null)
            return;

        _suppressAspectSync = true;
        _widthBox.Text = width.ToString();
        _heightBox.Text = height.ToString();
        _suppressAspectSync = false;

        UpdateEstimate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ÖNİZLEME KAPLAMASI
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seçili karenin kaplamalarını tuval üzerinde gösterir.
    /// Dışa aktarımdaki çizimin canlı karşılığıdır.
    /// </summary>
    private void DrawPreviewOverlay(EditorFrame frame)
    {
        OverlayCanvas.Children.Clear();

        double width = _document.Width * _zoom;
        double height = _document.Height * _zoom;
        OverlayCanvas.Width = width;
        OverlayCanvas.Height = height;

        DrawInputPreview(frame);
        DrawOverlaySetPreview(width, height);
    }

    private void DrawInputPreview(EditorFrame frame)
    {
        var input = frame.Input;

        bool clicking = input.Buttons != SfMouseButtons.None;
        bool drawClick = _clickHighlightCheck?.IsChecked == true && input.CursorVisible && clicking;
        bool drawCursor = _cursorHighlightCheck?.IsChecked == true && input.CursorVisible && !clicking;

        if (drawClick || drawCursor)
        {
            double radius = (_highlightRadiusSlider?.Value ?? 12) * _zoom;
            var circle = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(HighlightColor(input.Buttons)),
            };

            Canvas.SetLeft(circle, input.CursorX * _zoom - radius);
            Canvas.SetTop(circle, input.CursorY * _zoom - radius);
            OverlayCanvas.Children.Add(circle);
        }

        if (_showKeysCheck?.IsChecked != true || input.Keys.Count == 0)
            return;

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCD, 0x0C, 0x0E, 0x14)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = string.Join(" + ", input.Keys),
                Foreground = Brushes.White,
                FontSize = 12 * _zoom,
                FontWeight = FontWeights.SemiBold,
            },
        };

        Canvas.SetLeft(badge, 12 * _zoom);
        Canvas.SetBottom(badge, 12 * _zoom);
        OverlayCanvas.Children.Add(badge);
    }

    private void DrawOverlaySetPreview(double width, double height)
    {
        var set = BuildOverlaySet();

        if (set.Border.HasWork)
        {
            double thickness = set.Border.Thickness * _zoom;
            OverlayCanvas.Children.Add(new WpfRectangle
            {
                Width = Math.Max(0, width - thickness),
                Height = Math.Max(0, height - thickness),
                Stroke = Brushes.Black,
                StrokeThickness = thickness,
                Margin = new Thickness(thickness / 2),
            });
        }

        if (set.Progress.HasWork)
        {
            int count = Math.Max(1, _document.FrameCount);
            double fraction = count <= 1 ? 1 : (SelectedIndex + 1) / (double)count;

            if (set.Progress.Style == ProgressStyle.Bar)
            {
                double thickness = set.Progress.Thickness * _zoom;
                bool bottom = set.Progress.Placement is OverlayPlacement.BottomLeft
                    or OverlayPlacement.BottomCenter or OverlayPlacement.BottomRight;

                var bar = new WpfRectangle
                {
                    Width = width * fraction,
                    Height = thickness,
                    Fill = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
                };

                Canvas.SetLeft(bar, 0);
                Canvas.SetTop(bar, bottom ? height - thickness : 0);
                OverlayCanvas.Children.Add(bar);
            }
            else
            {
                // Yazı biçimi de önizlenmeli; yoksa ayar seçilince hiçbir şey görünmez.
                // Biçimlendirme dışa aktarımla aynı yerden gelir ki metin birebir eşleşsin.
                long elapsed = (long)_document.TimeUpTo(SelectedIndex).TotalMilliseconds;
                long total = (long)_document.Current.TotalDuration.TotalMilliseconds;

                string text = OverlayRenderer.FormatReadout(set.Progress,
                    SelectedIndex, count, elapsed, total, fraction);

                AddTextPreview(text, set.Progress.FontSize, set.Progress.Placement,
                    Colors.White, Color.FromArgb(170, 0, 0, 0), width, height, set.Progress.Margin);
            }
        }

        if (set.Caption.HasWork)
            AddTextPreview(set.Caption.Text, set.Caption.FontSize, set.Caption.Placement,
                Colors.White, Color.FromArgb(160, 0, 0, 0), width, height, set.Caption.Margin);

        if (!set.Watermark.HasWork)
            return;

        if (set.Watermark.HasImage)
            AddWatermarkImagePreview(set.Watermark, width, height);
        else
            AddTextPreview(set.Watermark.Text, set.Watermark.FontSize, set.Watermark.Placement,
                Color.FromArgb(140, 255, 255, 255), Colors.Transparent, width, height, set.Watermark.Margin);
    }

    /// <summary>Filigran logosunu tuvalde gösterir.</summary>
    private void AddWatermarkImagePreview(WatermarkOptions options, double width, double height)
    {
        BitmapImage source;

        try
        {
            source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(options.ImagePath!);
            source.EndInit();
            source.Freeze();
        }
        catch (Exception)
        {
            return;
        }

        double drawWidth = width * Math.Clamp(options.ImageScale, 0.02, 0.5);
        double drawHeight = drawWidth * (source.PixelHeight / (double)source.PixelWidth);

        var image = new WpfImage
        {
            Source = source,
            Width = drawWidth,
            Height = drawHeight,
            Opacity = Math.Clamp(options.ImageOpacity, 0, 1),
            Stretch = Stretch.Fill,
        };

        double margin = options.Margin * _zoom;

        double x = options.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.MiddleLeft or OverlayPlacement.BottomLeft => margin,
            OverlayPlacement.TopRight or OverlayPlacement.MiddleRight or OverlayPlacement.BottomRight
                => width - drawWidth - margin,
            _ => (width - drawWidth) / 2,
        };

        double y = options.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.TopCenter or OverlayPlacement.TopRight => margin,
            OverlayPlacement.MiddleLeft or OverlayPlacement.MiddleCenter or OverlayPlacement.MiddleRight
                => (height - drawHeight) / 2,
            _ => height - drawHeight - margin,
        };

        Canvas.SetLeft(image, Math.Clamp(x, 0, Math.Max(0, width - drawWidth)));
        Canvas.SetTop(image, Math.Clamp(y, 0, Math.Max(0, height - drawHeight)));
        OverlayCanvas.Children.Add(image);
    }

    private void AddTextPreview(string text, double fontSize, OverlayPlacement placement,
        Color foreground, Color background, double width, double height, double margin)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(foreground),
            FontSize = Math.Max(6, fontSize * _zoom),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(20, width - margin * 2 * _zoom),
        };

        var host = new Border
        {
            Background = background.A > 0 ? new SolidColorBrush(background) : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8 * _zoom, 4 * _zoom, 8 * _zoom, 4 * _zoom),
            Child = block,
        };

        host.Measure(new Size(width, height));
        var size = host.DesiredSize;
        double m = margin * _zoom;

        double x = placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.MiddleLeft or OverlayPlacement.BottomLeft => m,
            OverlayPlacement.TopRight or OverlayPlacement.MiddleRight or OverlayPlacement.BottomRight
                => width - size.Width - m,
            _ => (width - size.Width) / 2,
        };

        double y = placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.TopCenter or OverlayPlacement.TopRight => m,
            OverlayPlacement.MiddleLeft or OverlayPlacement.MiddleCenter or OverlayPlacement.MiddleRight
                => (height - size.Height) / 2,
            _ => height - size.Height - m,
        };

        Canvas.SetLeft(host, Math.Clamp(x, 0, Math.Max(0, width - size.Width)));
        Canvas.SetTop(host, Math.Clamp(y, 0, Math.Max(0, height - size.Height)));
        OverlayCanvas.Children.Add(host);
    }

    private static Color HighlightColor(SfMouseButtons buttons)
    {
        if ((buttons & SfMouseButtons.Right) != 0) return Color.FromArgb(120, 255, 0, 0);
        if ((buttons & SfMouseButtons.Middle) != 0) return Color.FromArgb(120, 0, 255, 255);
        if ((buttons & SfMouseButtons.Extra1) != 0) return Color.FromArgb(120, 255, 0, 128);
        if ((buttons & SfMouseButtons.Extra2) != 0) return Color.FromArgb(120, 255, 128, 0);
        return Color.FromArgb(120, 255, 255, 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DIŞA AKTARMA
    // ═══════════════════════════════════════════════════════════════════════════

    private OverlaySet BuildOverlaySet() => new()
    {
        Caption = new CaptionOptions
        {
            Enabled = _captionCheck?.IsChecked == true,
            Text = _captionBox?.Text ?? "",
            FontSize = _captionSizeSlider?.Value ?? 28,
            Placement = SelectedValue(_captionPlacement, OverlayPlacement.BottomCenter),
        },
        Progress = new ProgressOptions
        {
            Enabled = _progressCheck?.IsChecked == true,
            Style = SelectedValue(_progressStyleCombo, ProgressStyle.Bar),
            Readout = SelectedValue(_progressReadoutCombo, ProgressReadout.Seconds),
            Placement = SelectedValue(_progressPlacement, OverlayPlacement.BottomLeft),
            Thickness = _progressThicknessSlider?.Value ?? 6,
            SecondsDecimals = SelectedValue(_progressDecimalsCombo, 1),
        },
        Border = new BorderOptions
        {
            Enabled = _borderCheck?.IsChecked == true,
            Thickness = _borderThicknessSlider?.Value ?? 2,
            Color = DrawingColor.Black,
        },
        Watermark = new WatermarkOptions
        {
            Enabled = _watermarkCheck?.IsChecked == true,
            Text = _watermarkBox?.Text ?? "",
            ImagePath = _watermarkImagePath,
            ImageScale = (_watermarkScaleSlider?.Value ?? 12) / 100.0,
            Placement = SelectedValue(_watermarkPlacement, OverlayPlacement.BottomRight),
        },
    };

    private async Task ExportAsync()
    {
        if (_exporting || _document.FrameCount == 0)
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Animasyonlu GIF|*.gif",
            DefaultExt = ".gif",
            FileName = $"kayit_{DateTime.Now:yyyyMMdd_HHmmss}",
            InitialDirectory = Directory.Exists(_settings?.SaveDirectory) ? _settings!.SaveDirectory : null,
        };

        if (dialog.ShowDialog() != true)
            return;

        StopPlayback();
        ClampRange();

        _exporting = true;
        _exportCts = new CancellationTokenSource();

        SidePanel.IsEnabled = false;
        ExportButton.IsEnabled = false;
        _cancelExportButton!.Visibility = Visibility.Visible;
        ExportProgress.Value = 0;
        ExportProgress.Visibility = Visibility.Visible;
        SetStatus("Kaydediliyor…");

        try
        {
            var token = _exportCts.Token;

            int count = _rangeEnd - _rangeStart + 1;
            var slice = _document.Frames.Skip(_rangeStart).Take(count).ToList();

            int outWidth = ParseOr(_widthBox?.Text, _document.Width);
            int outHeight = ParseOr(_heightBox?.Text, _document.Height);
            bool resize = outWidth != _document.Width || outHeight != _document.Height;

            var pixels = slice.Select(f => f.Pixels).ToList();
            if (resize)
            {
                SetStatus("Yeniden boyutlandırılıyor…");
                int sw = _document.Width, sh = _document.Height;
                pixels = await Task.Run(
                    () => pixels.Select(p => ImageOperations.ResizePixels(p, sw, sh, outWidth, outHeight)).ToList(),
                    token);
                SetStatus("Kaydediliyor…");
            }

            // Çizim katmanları karelere kalıcı yazılmaz; burada işlenir.
            if (AnnotationCompositor.HasWork(_track))
            {
                SetStatus("Katmanlar işleniyor…");
                int start = _rangeStart;
                pixels = await Task.Run(() => ComposeForExport(pixels, start, outWidth, outHeight, token), token);
                SetStatus("Kaydediliyor…");
            }

            bool highlightClicks = _clickHighlightCheck?.IsChecked == true;
            bool highlightCursor = _cursorHighlightCheck?.IsChecked == true;
            bool showKeys = _showKeysCheck?.IsChecked == true;
            var overlays = BuildOverlaySet();

            var options = new GifExportOptions
            {
                Frames = pixels,
                FrameDelays = slice.Select(f => f.Delay).ToList(),
                FrameInputs = slice.Select(f => f.Input).ToList(),
                Width = outWidth,
                Height = outHeight,
                ColorCount = SelectedValue(_colorCombo, 256),
                QuantizerType = SelectedValue(_quantizerCombo, QuantizerType.Neural),
                SamplingFactor = (int)(_samplingSlider?.Value ?? 5),
                UseGlobalPalette = _globalPaletteCheck?.IsChecked == true,
                Dithering = _ditheringCheck?.IsChecked == true,
                OptimizeUnchangedPixels = _optimizeCheck?.IsChecked != false,
                ChangeTolerance = (int)(_toleranceSlider?.Value ?? 0),
                InputOverlay = highlightClicks || highlightCursor || showKeys
                    ? new InputOverlayOptions
                    {
                        HighlightClicks = highlightClicks,
                        HighlightCursor = highlightCursor,
                        ShowKeys = showKeys,
                        Radius = _highlightRadiusSlider?.Value ?? 12,
                    }
                    : null,
                Overlays = overlays.HasWork ? overlays : null,
            };

            await _recorder.SaveAsync(dialog.FileName, options, progress: p =>
                Dispatcher.Invoke(() =>
                {
                    ExportProgress.Value = p * 100;
                    SetStatus($"Kaydediliyor… %{p * 100:0}");
                }), token);

            PersistSettings();

            long size = new FileInfo(dialog.FileName).Length;
            ShowToast($"Kaydedildi → {System.IO.Path.GetFileName(dialog.FileName)} ({FormatBytes(size)})");
        }
        catch (OperationCanceledException)
        {
            ShowToast("Kaydetme iptal edildi");
        }
        catch (Exception ex)
        {
            ShowToast("GIF kaydedilemedi");
            MessageBox.Show(this, "GIF kaydedilemedi: " + ex.Message, "ScreenForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _exporting = false;
            _exportCts?.Dispose();
            _exportCts = null;

            SidePanel.IsEnabled = true;
            ExportProgress.Visibility = Visibility.Collapsed;
            _cancelExportButton.Visibility = Visibility.Collapsed;
            UpdateChrome();
        }
    }

    private void CancelExport()
    {
        if (!_exporting)
            return;

        _exportCts?.Cancel();
        SetStatus("İptal ediliyor…");
    }

    private void PersistSettings()
    {
        if (_settings == null)
            return;

        var gif = _settings.Gif;
        gif.ColorCount = SelectedValue(_colorCombo, 256);
        gif.Quantizer = SelectedValue(_quantizerCombo, QuantizerType.Neural).ToString();
        gif.SamplingFactor = (int)(_samplingSlider?.Value ?? 5);
        gif.Dithering = _ditheringCheck?.IsChecked == true;
        gif.UseGlobalPalette = _globalPaletteCheck?.IsChecked == true;
        gif.OptimizeUnchangedPixels = _optimizeCheck?.IsChecked != false;
        gif.ChangeTolerance = (int)(_toleranceSlider?.Value ?? 0);
        gif.HighlightClicks = _clickHighlightCheck?.IsChecked == true;
        gif.HighlightCursor = _cursorHighlightCheck?.IsChecked == true;
        gif.ShowKeys = _showKeysCheck?.IsChecked == true;
        gif.HighlightRadius = _highlightRadiusSlider?.Value ?? 12;

        _settings.Save();
    }

    /// <summary>Ayarların etkisini anında göstermek için kaba boyut tahmini.</summary>
    private void UpdateEstimate()
    {
        if (EstimateLabel == null || _document.FrameCount == 0)
            return;

        int frames = Math.Max(1, _rangeEnd - _rangeStart + 1);
        int width = ParseOr(_widthBox?.Text, _document.Width);
        int height = ParseOr(_heightBox?.Text, _document.Height);
        int colors = SelectedValue(_colorCombo, 256);

        double bitsPerPixel = Math.Max(2, Math.Ceiling(Math.Log2(colors)));
        double bytes = width * (double)height * frames * bitsPerPixel / 8.0;

        double compression = 0.32;
        if (_optimizeCheck?.IsChecked == true) compression *= 0.35;
        if (_globalPaletteCheck?.IsChecked == true) compression *= 0.9;
        if (_toleranceSlider != null) compression *= 1.0 - Math.Min(0.25, _toleranceSlider.Value / 128.0);

        bytes *= compression;
        if (_globalPaletteCheck?.IsChecked != true)
            bytes += frames * colors * 3.0;

        EstimateLabel.Text = $"~{FormatBytes((long)bytes)}";
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MB" : $"{bytes / 1024.0:0} KB";

    private static int ParseOr(string? text, int fallback)
        => int.TryParse(text, out int value) && value > 0 ? value : fallback;

    // ═══════════════════════════════════════════════════════════════════════════
    //  DENETİM FABRİKALARI
    // ═══════════════════════════════════════════════════════════════════════════

    private TextBlock Header(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("EditorSectionHeader"),
    };

    private Border Separator() => new()
    {
        Height = 1,
        Background = (Brush)FindResource("BorderBrush"),
        Margin = new Thickness(0, 10, 0, 10),
    };

    /// <summary>
    /// Başlığa tıklayınca açılıp kapanan bölüm. İleri düzey ayarları varsayılan
    /// olarak gizleyerek paneli sade tutar.
    /// </summary>
    private StackPanel Collapsible(string title, UIElement content, bool expanded = false)
    {
        var header = new Button
        {
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 11,
            Padding = new Thickness(0, 4, 0, 4),
            Cursor = Cursors.Hand,
            Content = (expanded ? "▾  " : "▸  ") + title,
        };

        content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        header.Click += (_, _) =>
        {
            expanded = !expanded;
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            header.Content = (expanded ? "▾  " : "▸  ") + title;
        };

        var wrap = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        wrap.Children.Add(header);
        wrap.Children.Add(content);
        return wrap;
    }

    private Button ToolButton(string text, string? tooltip = null) => new()
    {
        Content = text,
        Style = (Style)FindResource("EditorToolButton"),
        ToolTip = tooltip,
        Margin = new Thickness(2, 3, 2, 3),
    };

    private CheckBox Check(string text, bool isChecked, string? tooltip = null) => new()
    {
        Content = new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        },
        IsChecked = isChecked,
        ToolTip = tooltip,
        Margin = new Thickness(0, 3, 0, 3),
    };

    private TextBox NumberBox(string value) => new()
    {
        Text = value,
        Width = 62,
        Style = (Style)FindResource("EditorTextBox"),
        Margin = new Thickness(0, 3, 4, 3),
    };

    private ComboBox Combo<T>(params (string Label, T Value)[] items)
    {
        var combo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            Height = 26,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2),
        };

        foreach (var (label, value) in items)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        combo.SelectedIndex = 0;
        return combo;
    }

    private ComboBox PlacementCombo(OverlayPlacement initial)
    {
        var combo = Combo(
            ("Üst sol", OverlayPlacement.TopLeft),
            ("Üst orta", OverlayPlacement.TopCenter),
            ("Üst sağ", OverlayPlacement.TopRight),
            ("Orta sol", OverlayPlacement.MiddleLeft),
            ("Merkez", OverlayPlacement.MiddleCenter),
            ("Orta sağ", OverlayPlacement.MiddleRight),
            ("Alt sol", OverlayPlacement.BottomLeft),
            ("Alt orta", OverlayPlacement.BottomCenter),
            ("Alt sağ", OverlayPlacement.BottomRight));

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag is OverlayPlacement p && p == initial)
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        return combo;
    }

    private Grid LabeledRow(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock { Text = label, Style = (Style)FindResource("EditorFieldLabel") };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>Etiket + değer + kaydırıcıdan oluşan satır ekler.</summary>
    private (Slider Slider, TextBlock Label) SliderRow(WpfPanel host, string label,
        double min, double max, double value, string? tooltip = null)
    {
        var valueLabel = new TextBlock
        {
            Text = ((int)value).ToString(CultureInfo.InvariantCulture),
            Foreground = (Brush)FindResource("AccentBrush"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Style = (Style)FindResource("ToolSlider"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
        };

        slider.ValueChanged += (_, e) => valueLabel.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);

        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock { Text = label, Style = (Style)FindResource("EditorFieldLabel") };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(valueLabel, 1);
        Grid.SetColumn(slider, 2);
        slider.Margin = new Thickness(6, 0, 0, 0);

        grid.Children.Add(text);
        grid.Children.Add(valueLabel);
        grid.Children.Add(slider);
        host.Children.Add(grid);

        return (slider, valueLabel);
    }

    private static T SelectedValue<T>(ComboBox? combo, T fallback)
        => combo?.SelectedItem is ComboBoxItem item && item.Tag is T value ? value : fallback;
}
