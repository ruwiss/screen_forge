using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenForge.Editor;
using ScreenForge.Gif;
using ScreenForge.Gif.Editing;
using ScreenForge.Settings;
using WpfImage = System.Windows.Controls.Image;

namespace ScreenForge.Windows;

/// <summary>
/// GIF kaydı bittikten sonra açılan düzenleyici.
/// Kare düzenleme, kaplama ayarları ve dışa aktarmayı tek pencerede toplar.
/// </summary>
public sealed partial class GifEditorWindow : Window
{
    /// <summary>Zaman çizelgesindeki tek bir kare kutusu.</summary>
    private sealed class TimelineItem : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;
        private int _number;
        private int _delay;

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; Raise(nameof(Thumbnail)); }
        }

        public int Number
        {
            get => _number;
            set { _number = value; Raise(nameof(Number)); }
        }

        public int Delay
        {
            get => _delay;
            set { _delay = value; Raise(nameof(Delay)); Raise(nameof(DelayText)); }
        }

        public string DelayText => $"{_delay} ms";

        /// <summary>Kare çoğaltmayla üretildi mi; şeritte ayırt edilir.</summary>
        public bool IsDuplicate
        {
            get => _isDuplicate;
            set { _isDuplicate = value; Raise(nameof(IsDuplicate)); }
        }

        private bool _isDuplicate;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private const int ThumbnailWidth = 76;

    private readonly GifRecorder _recorder;
    private readonly AppSettings? _settings;
    private readonly EditorDocument _document;
    private readonly ThumbnailCache _thumbnails = new(ThumbnailWidth);
    private readonly ObservableCollection<TimelineItem> _timelineItems = new();

    /// <summary>Kare ve çizim düzenlemelerinin ortak sırası.</summary>
    private readonly EditHistory _history = new();

    // Kırpma aralığı (dahil)
    private int _rangeStart;
    private int _rangeEnd;

    // Oynatma
    private DispatcherTimer? _playTimer;
    private bool _playing;

    // Dışa aktarma
    private bool _exporting;
    private CancellationTokenSource? _exportCts;

    private double _zoom = 1.0;
    private bool _suppressSelectionSync;
    private bool _fitOnNextLayout = true;

    /// <summary>Uzun süren bir işlem sürerken yeni düzenlemeleri engeller.</summary>
    private bool _busy;

    public GifEditorWindow(GifRecorder recorder, AppSettings? settings = null)
    {
        _recorder = recorder;
        _settings = settings;

        var recording = recorder.DetachFrames();
        var frames = new List<EditorFrame>(recording.Frames.Count);

        for (int i = 0; i < recording.Frames.Count; i++)
        {
            frames.Add(new EditorFrame
            {
                Pixels = recording.Frames[i],
                Delay = i < recording.FrameDelays.Count ? recording.FrameDelays[i] : 100,
                Input = i < recording.Inputs.Count ? recording.Inputs[i] : new Gif.Input.FrameInput(),
            });
        }

        _document = new EditorDocument(frames, recorder.Width, recorder.Height);
        _rangeEnd = Math.Max(0, frames.Count - 1);

        // Çizim stilleri ekran alıntısı araçlarıyla ortak hatırlanır.
        _toolStyle = settings?.ToolStyles ?? new ToolStyleMemory();

        InitializeComponent();

        _document.Changed += OnDocumentChanged;
        BuildPanels();
        WireEvents();
        WireAnnotations();

        Timeline.ItemsSource = _timelineItems;
        RebuildTimeline();

        if (_document.FrameCount > 0)
            SelectFrame(0);

        UpdateChrome();
    }

    private int SelectedIndex => Timeline.SelectedIndex < 0 ? 0 : Timeline.SelectedIndex;

    /// <summary>Seçili kare indeksleri; seçim yoksa geçerli kare.</summary>
    private List<int> SelectedIndexes()
    {
        var indexes = Timeline.SelectedItems
            .Cast<TimelineItem>()
            .Select(item => _timelineItems.IndexOf(item))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();

        if (indexes.Count == 0 && _document.FrameCount > 0)
            indexes.Add(SelectedIndex);

        return indexes;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  OLAY BAĞLAMA
    // ═══════════════════════════════════════════════════════════════════════════

    private void WireEvents()
    {
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        };

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();

        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();

        DeleteButton.Click += (_, _) => DeleteSelected();
        DuplicateButton.Click += (_, _) => DuplicateSelected();

        ReverseButton.Click += (_, _) => Edit("Ters çevir", FrameOperations.Reverse);

        // Zaman çizelgesi bağlam menüsü — kare işlemleri kareye ait yerde.
        MenuDuplicate.Click += (_, _) => DuplicateSelected();
        MenuDelete.Click += (_, _) => DeleteSelected();
        MenuMoveLeft.Click += (_, _) => MoveSelection(left: true);
        MenuMoveRight.Click += (_, _) => MoveSelection(left: false);
        MenuDeleteBefore.Click += (_, _) => DeleteBefore();
        MenuDeleteAfter.Click += (_, _) => DeleteAfter();
        MenuMarkIn.Click += (_, _) => MarkRangeStart();
        MenuMarkOut.Click += (_, _) => MarkRangeEnd();

        RotateLeftButton.Click += async (_, _) => await RotateAsync(RotateDirection.Left90);
        RotateRightButton.Click += async (_, _) => await RotateAsync(RotateDirection.Right90);
        CropButton.Click += (_, _) => BeginCrop();
        CropApplyButton.Click += async (_, _) => await ApplyCropAsync();
        CropCancelButton.Click += (_, _) => CancelCrop();

        ExportButton.Click += async (_, _) => await ExportAsync();

        FirstFrameButton.Click += (_, _) => { StopPlayback(); SelectFrame(0); };
        PrevFrameButton.Click += (_, _) => { StopPlayback(); StepFrame(-1); };
        PlayButton.Click += (_, _) => TogglePlayback();
        NextFrameButton.Click += (_, _) => { StopPlayback(); StepFrame(1); };
        LastFrameButton.Click += (_, _) => { StopPlayback(); SelectFrame(_document.FrameCount - 1); };

        MarkInButton.Click += (_, _) => MarkRangeStart();
        MarkOutButton.Click += (_, _) => MarkRangeEnd();
        RangeResetButton.Click += (_, _) => { ResetRange(); UpdateChrome(); };
        TrimButton.Click += (_, _) => TrimToRange();

        ZoomSlider.ValueChanged += (_, e) => ApplyZoom(e.NewValue / 100.0, fromSlider: true);
        ZoomFitButton.Click += (_, _) => ZoomToFit();

        Timeline.SelectionChanged += OnTimelineSelectionChanged;
        Timeline.MouseDoubleClick += (_, _) => StopPlayback();

        CanvasScroll.PreviewMouseWheel += OnCanvasWheel;
        CanvasScroll.SizeChanged += (_, _) => { if (_fitOnNextLayout) ZoomToFit(); };

        // İlk sığdırma yerleşim tamamlandıktan sonra yapılmalı; aksi hâlde
        // görüntü alanı henüz 0 olduğu için ölçek hesaplanamaz.
        Loaded += (_, _) => Dispatcher.BeginInvoke(ZoomToFit, DispatcherPriority.Loaded);

        CropCanvas.MouseLeftButtonDown += OnCropMouseDown;
        CropCanvas.MouseMove += OnCropMouseMove;
        CropCanvas.MouseLeftButtonUp += OnCropMouseUp;

        PreviewKeyDown += OnWindowKeyDown;
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            StopPlayback();
            _exportCts?.Cancel();
            _exportCts?.Dispose();
            _recorder.Dispose();
        };
    }

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exporting)
            e.Cancel = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DÜZENLEME
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Belgeye bir dönüşüm uygular ve geri alma adımı kaydeder.</summary>
    /// <param name="changesFrameCount">
    /// İşlem kare sayısını değiştirmeyi amaçlıyorsa <see langword="true"/>.
    /// Sonuç değişmezse kullanıcıya "değişiklik yok" bildirilir.
    /// </param>
    private void Edit(string label, Func<IReadOnlyList<EditorFrame>, List<EditorFrame>> transform,
        bool changesFrameCount = false, Action<int>? trackShift = null)
    {
        if (_document.FrameCount == 0)
            return;

        StopPlayback();

        int anchor = SelectedIndex;
        var result = transform(_document.Frames);

        if (result.Count == 0)
        {
            SetStatus("İşlem en az bir kare bırakmalı");
            return;
        }

        int before = _document.FrameCount;
        _document.Apply(label, result);
        _history.Record(EditScope.Frames);

        int delta = _document.FrameCount - before;

        // Katman aralıkları kare eklenip silindiğinde kaymalı. Birden çok
        // ekleme noktası olan işlemler (ör. çoğaltma) kendi kesin kaydırma
        // stratejisini sağlar; aksi halde seçili kaynak kare de ötelenir.
        if (delta != 0)
        {
            if (trackShift != null)
                trackShift(delta);
            else
                ShiftTracks(anchor, delta);
        }

        // Kare sayısı yalnızca gerçekten değiştiyse söylenir; her işlemde
        // sayı yazmak durum çubuğunu gereksiz gürültüye boğuyordu.
        ShowToast(delta switch
        {
            > 0 => $"{label} · +{delta} kare",
            < 0 => $"{label} · {-delta} kare silindi",

            // Kare sayısı değişmediyse işlem ya süreleri değiştirdi ya da
            // silinecek bir şey bulamadı; kullanıcı hangisi olduğunu bilmeli.
            _ when changesFrameCount => $"{label} · değişiklik yok",
            _ => label,
        });

        SelectFrame(Math.Min(anchor, _document.FrameCount - 1));
    }

    /// <summary>
    /// Her kareyi yeniden çizen ağır dönüşümleri arka planda çalıştırır.
    /// </summary>
    /// <remarks>
    /// Döndürme, aynalama ve kırpma her pikseli dolaşır. 1920×1080 ve 60 karede
    /// bu yüz milyonlarca piksel demektir; arayüz iş parçacığında yapılırsa
    /// program yanıt vermez.
    /// </remarks>
    private async Task EditPixelsAsync(string label,
        Func<IReadOnlyList<EditorFrame>, List<EditorFrame>> transform, int width, int height)
    {
        if (_document.FrameCount == 0 || _busy)
            return;

        StopPlayback();
        int anchor = SelectedIndex;

        var frames = _document.Frames;
        BeginBusy($"{label}…");

        try
        {
            var result = await Task.Run(() => transform(frames)).ConfigureAwait(false);

            // Sonuç arayüze uygulanmalı. ConfigureAwait(false) sonrası havuz
            // iş parçacığındayız; devamı açıkça dağıtıcıya taşınır.
            await Dispatcher.InvokeAsync(() =>
            {
                _document.Apply(label, result, width, height);
                _history.Record(EditScope.Frames);
                SetStatus($"{label} · {width}×{height}");
                SelectFrame(Math.Min(anchor, _document.FrameCount - 1));
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(EndBusy);
        }
    }

    /// <summary>Uzun süren işlem boyunca girdiyi kilitler ve durum gösterir.</summary>
    private void BeginBusy(string status)
    {
        _busy = true;
        Toolbar.IsEnabled = false;
        SidePanel.IsEnabled = false;
        Timeline.IsEnabled = false;
        Cursor = Cursors.Wait;
        BusyBar.Visibility = Visibility.Visible;
        SetStatus(status);
    }

    private void EndBusy()
    {
        _busy = false;
        Toolbar.IsEnabled = true;
        SidePanel.IsEnabled = true;
        Timeline.IsEnabled = true;
        Cursor = Cursors.Arrow;
        BusyBar.Visibility = Visibility.Collapsed;
        UpdateChrome();
    }

    /// <summary>
    /// Bağlama göre siler: çizim nesnesi seçiliyse nesneyi, değilse kareyi.
    /// </summary>
    /// <summary>
    /// En son yapılan işlemi geri alır — kare mi çizim mi olduğuna bakmaksızın.
    /// </summary>
    private void Undo()
    {
        StopPlayback();

        switch (_history.PopUndo())
        {
            case EditScope.Frames:
                string label = _document.UndoLabel ?? "Kare düzenlemesi";
                _document.Undo();
                ShowToast($"Geri alındı: {label}");
                break;

            case EditScope.Annotation:
                UndoAnnotation();
                ShowToast("Geri alındı: çizim");
                break;

            default:
                return;
        }

        UpdateChrome();
    }

    private void Redo()
    {
        StopPlayback();

        switch (_history.PopRedo())
        {
            case EditScope.Frames:
                _document.Redo();
                ShowToast("Yinelendi: kare düzenlemesi");
                break;

            case EditScope.Annotation:
                RedoAnnotation();
                ShowToast("Yinelendi: çizim");
                break;

            default:
                return;
        }

        UpdateChrome();
    }

    private void DeleteSelected()
    {
        if (TryDeleteAnnotationSelection())
            return;

        var selection = SelectedIndexes();
        if (selection.Count >= _document.FrameCount)
        {
            SetStatus("Tüm kareler silinemez");
            return;
        }

        Edit($"{selection.Count} kare sil", f => FrameOperations.Remove(f, selection, CurrentDelayMode()));
    }

    /// <summary>Bağlama göre çoğaltır: çizim nesnesi seçiliyse nesneyi, değilse kareyi.</summary>
    private void DuplicateSelected()
    {
        if (TryDuplicateAnnotationSelection())
            return;

        var sources = SelectedIndexes();
        Edit("Çoğalt", f => FrameOperations.Duplicate(f, sources),
            trackShift: _ => ShiftTracksForDuplicatedFrames(sources));

        // Çoğaltılan kareler kaynağın çizimlerini de göstermeli.
        ExtendClipsForDuplicatedFrames(sources);
        MarkFramesDuplicated(sources);
        ShowToast(sources.Count == 1 ? "Kare çoğaltıldı" : $"{sources.Count} kare çoğaltıldı");
    }

    private void DeleteBefore()
        => Edit("Öncekileri sil", f => FrameOperations.RemoveBefore(f, SelectedIndexes().Min()));

    private void DeleteAfter()
        => Edit("Sonrakileri sil", f => FrameOperations.RemoveAfter(f, SelectedIndexes().Max()));

    private void MarkRangeStart()
    {
        _rangeStart = Math.Min(SelectedIndex, _rangeEnd);
        UpdateChrome();
    }

    private void MarkRangeEnd()
    {
        _rangeEnd = Math.Max(SelectedIndex, _rangeStart);
        UpdateChrome();
    }

    private void MoveSelection(bool left)
    {
        var selection = SelectedIndexes();
        Edit(left ? "Sola taşı" : "Sağa taşı",
            f => left ? FrameOperations.MoveLeft(f, selection) : FrameOperations.MoveRight(f, selection));

        // Seçim kayan karelerle birlikte gitsin.
        var moved = selection.Select(i => Math.Clamp(left ? i - 1 : i + 1, 0, _document.FrameCount - 1)).ToList();
        SelectFrames(moved);
    }

    private async Task RotateAsync(RotateDirection direction)
    {
        int sw = _document.Width, sh = _document.Height;
        var (w, h) = ImageOperations.SizeAfterRotate(sw, sh, direction);

        await EditPixelsAsync(direction == RotateDirection.Left90 ? "Sola döndür" : "Sağa döndür",
            f => ImageOperations.Rotate(f, sw, sh, direction), w, h);
    }

    private void TrimToRange()
    {
        ClampRange();

        if (_rangeStart == 0 && _rangeEnd == _document.FrameCount - 1)
        {
            SetStatus("Aralık tüm kareleri kapsıyor");
            return;
        }

        int start = _rangeStart, end = _rangeEnd;
        Edit("Aralığa kırp", f => FrameOperations.Trim(f, start, end));
        ResetRange();
        SelectFrame(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BELGE DEĞİŞİMİ
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnDocumentChanged()
    {
        ClampRange();
        RebuildTimeline();
        UpdateChrome();
        UpdatePreview();
    }

    /// <summary>
    /// Zaman çizelgesini belgeyle eşitler.
    /// </summary>
    /// <remarks>
    /// Küçük resimler piksel dizisine göre önbelleklendiği için sıralama
    /// işlemleri (ters çevirme, taşıma, silme, çoğaltma) yeniden çizim
    /// gerektirmez; yalnızca kutuların içeriği güncellenir.
    /// </remarks>
    private void RebuildTimeline()
    {
        var frames = _document.Frames;
        _thumbnails.SetFrameSize(_document.Width, _document.Height);

        // Kutu sayısını eşitle. Koleksiyon gözlemlenebilir olduğu için
        // Items.Refresh() gerekmez; tam yenileme tüm kapsayıcıları atardı.
        while (_timelineItems.Count > frames.Count)
            _timelineItems.RemoveAt(_timelineItems.Count - 1);

        while (_timelineItems.Count < frames.Count)
            _timelineItems.Add(new TimelineItem());

        for (int i = 0; i < frames.Count; i++)
        {
            var item = _timelineItems[i];
            item.Number = i + 1;
            item.Delay = frames[i].Delay;
            item.Thumbnail = _thumbnails.Get(frames[i].Pixels);
            item.IsDuplicate = _duplicatedPixels.Contains(frames[i].Pixels);
        }
    }

    private void UpdatePreview()
    {
        if (_document.FrameCount == 0)
        {
            PreviewImage.Source = null;
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;

        int index = Math.Clamp(SelectedIndex, 0, _document.FrameCount - 1);
        var frame = _document.Frames[index];

        // Etkin katman tuvalde canlı düzenlendiği için burada yalnızca
        // diğer katmanlar karenin üzerine işlenir; aksi hâlde çizim iki kez görünür.
        var pixels = ComposePreviewPixels(frame.Pixels, index);

        var source = BitmapSource.Create(_document.Width, _document.Height, 96, 96,
            PixelFormats.Bgra32, null, pixels, _document.Width * 4);
        source.Freeze();

        PreviewImage.Source = source;
        ApplyZoom(_zoom);
        DrawPreviewOverlay(frame);
                RefreshClips();
        UpdateAnnotationVisibility();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SEÇİM
    // ═══════════════════════════════════════════════════════════════════════════

    private void SelectFrame(int index)
    {
        if (_document.FrameCount == 0)
            return;

        index = Math.Clamp(index, 0, _document.FrameCount - 1);

        _suppressSelectionSync = true;
        Timeline.SelectedIndex = index;
        _suppressSelectionSync = false;

        Timeline.ScrollIntoView(_timelineItems[index]);
        UpdatePreview();
        UpdateChrome();
    }

    private void SelectFrames(IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
            return;

        _suppressSelectionSync = true;
        Timeline.SelectedItems.Clear();

        foreach (int i in indexes.Where(i => i >= 0 && i < _timelineItems.Count))
            Timeline.SelectedItems.Add(_timelineItems[i]);

        _suppressSelectionSync = false;

        Timeline.ScrollIntoView(_timelineItems[Math.Clamp(indexes[0], 0, _timelineItems.Count - 1)]);
        UpdatePreview();
        UpdateChrome();
    }

    private void StepFrame(int delta) => SelectFrame(SelectedIndex + delta);

    /// <summary>
    /// Çoğaltmayla üretilen kareleri işaretler.
    /// </summary>
    /// <remarks>
    /// Kopya kareler kaynağıyla birebir aynı göründüğü için şeritte
    /// ayırt edilmeleri gerekir; aksi hâlde hangisinin eklendiği anlaşılmaz.
    /// </remarks>
    private void MarkFramesDuplicated(IReadOnlyList<int> sourceFrames)
    {
        for (int i = 0; i < sourceFrames.Count; i++)
        {
            // Kopya, kaynağın hemen ardındadır; önceki eklemeler indisi kaydırır.
            int copy = sourceFrames[i] + i + 1;

            if (copy >= 0 && copy < _timelineItems.Count)
                _duplicatedPixels.Add(_document.Frames[copy].Pixels);
        }

        RefreshDuplicateMarks();
    }

    /// <summary>Çoğaltma sonucu üretilen kare pikselleri.</summary>
    private readonly HashSet<byte[]> _duplicatedPixels = new(ReferenceEqualityComparer.Instance);

    private void RefreshDuplicateMarks()
    {
        var frames = _document.Frames;

        for (int i = 0; i < _timelineItems.Count && i < frames.Count; i++)
            _timelineItems[i].IsDuplicate = _duplicatedPixels.Contains(frames[i].Pixels);
    }

    // ─── Kare üzerindeki eylem düğmeleri ──────────────────────────────────────

    private void FrameDuplicate_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        int index = IndexOfChip(sender);
        if (index < 0)
            return;

        var sources = new[] { index };
        Edit("Kare çoğalt", f => FrameOperations.Duplicate(f, sources),
            trackShift: _ => ShiftTracksForDuplicatedFrames(sources));
        ExtendClipsForDuplicatedFrames(sources);
        MarkFramesDuplicated(sources);
        SelectFrame(index + 1);
    }

    private void FrameDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        int index = IndexOfChip(sender);
        if (index < 0 || _document.FrameCount <= 1)
            return;

        Edit("Kare sil", f => FrameOperations.Remove(f, new[] { index }, CurrentDelayMode()));
        SelectFrame(Math.Min(index, _document.FrameCount - 1));
    }

    /// <summary>Kare üzerindeki düğmenin ait olduğu kare sırasını bulur.</summary>
    private int IndexOfChip(object sender)
        => sender is FrameworkElement { Tag: TimelineItem item } ? _timelineItems.IndexOf(item) : -1;

    private void OnTimelineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync)
            return;

        UpdatePreview();
        UpdateChrome();
    }

    // ─── Aralık ───────────────────────────────────────────────────────────────

    private void ResetRange()
    {
        _rangeStart = 0;
        _rangeEnd = Math.Max(0, _document.FrameCount - 1);
    }

    private void ClampRange()
    {
        int last = Math.Max(0, _document.FrameCount - 1);
        _rangeEnd = Math.Clamp(_rangeEnd, 0, last);
        _rangeStart = Math.Clamp(_rangeStart, 0, _rangeEnd);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  OYNATMA
    // ═══════════════════════════════════════════════════════════════════════════

    private void TogglePlayback()
    {
        if (_playing) StopPlayback();
        else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_document.FrameCount < 2)
            return;

        _playing = true;
        PlayIcon.Data = (Geometry)FindResource("IconPause");
        PlayIcon.Fill = Brushes.Transparent;

        _playTimer ??= new DispatcherTimer(DispatcherPriority.Render);
        if (_playTimer.Tag == null)
        {
            _playTimer.Tag = true;
            _playTimer.Tick += (_, _) => AdvancePlayback();
        }

        _playTimer.Interval = TimeSpan.FromMilliseconds(CurrentDelay(SelectedIndex));
        _playTimer.Start();
    }

    private void AdvancePlayback()
    {
        if (!_playing || _document.FrameCount == 0)
        {
            StopPlayback();
            return;
        }

        int next = SelectedIndex + 1;

        // Aralık işaretlenmişse yalnızca o bölümü oynat.
        if (next > _rangeEnd || next >= _document.FrameCount)
        {
            if (LoopCheck.IsChecked != true)
            {
                StopPlayback();
                return;
            }

            next = _rangeStart;
        }

        SelectFrame(next);
        _playTimer!.Interval = TimeSpan.FromMilliseconds(CurrentDelay(next));
    }

    private void StopPlayback()
    {
        if (!_playing)
            return;

        _playing = false;
        _playTimer?.Stop();
        PlayIcon.Data = (Geometry)FindResource("IconPlay");
        PlayIcon.Fill = (Brush)FindResource("AccentBrush");
    }

    private int CurrentDelay(int index)
        => index >= 0 && index < _document.FrameCount ? Math.Max(10, _document.Frames[index].Delay) : 100;

    // ═══════════════════════════════════════════════════════════════════════════
    //  YAKINLAŞTIRMA
    // ═══════════════════════════════════════════════════════════════════════════

    private void ApplyZoom(double zoom, bool fromSlider = false)
    {
        _zoom = Math.Clamp(zoom, 0.1, 4.0);

        if (!fromSlider && Math.Abs(ZoomSlider.Value - _zoom * 100) > 0.5)
        {
            ZoomSlider.ValueChanged -= OnZoomSliderChanged;
            ZoomSlider.Value = _zoom * 100;
            ZoomSlider.ValueChanged += OnZoomSliderChanged;
        }

        double width = _document.Width * _zoom;
        double height = _document.Height * _zoom;

        // Görüntü, kaplama ve kırpma tuvali aynı boyutta olmalı; kırpma
        // koordinatları bu eşitliğe dayanır.
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        CanvasSurface.Width = width;
        CanvasSurface.Height = height;
        LayoutAnnotationCanvas(width, height);

        // Satır içi metin düzenlenirken yakınlaştırma değişirse caret katmanı
        // de canlı çizilen TextItem ile aynı ölçekte kalmalı.
        if (_textBox != null && _editingTextItem != null)
        {
            _textBox.FontSize = Math.Max(8, _editingTextItem.FontSize * _zoom);
            PlaceTextBox();
        }

        ZoomLabel.Text = $"%{_zoom * 100:0}";

        if (fromSlider)
            _fitOnNextLayout = false;
    }

    private void OnZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyZoom(e.NewValue / 100.0, fromSlider: true);

    private void ZoomToFit()
    {
        if (_document.Width <= 0 || _document.Height <= 0)
            return;

        // Kenar boşluğu + kenarlık payı düşülür ki görüntü kırpılmadan otursun.
        double available = CanvasScroll.ViewportWidth - 44;
        double availableHeight = CanvasScroll.ViewportHeight - 44;

        if (available <= 0 || availableHeight <= 0)
            return;

        // Büyütme yok; küçük kayıtlar gerçek boyutunda kalsın.
        double fit = Math.Min(1.0, Math.Min(available / _document.Width, availableHeight / _document.Height));
        ApplyZoom(fit);
        _fitOnNextLayout = true;
    }

    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
            return;

        ApplyZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DURUM ÇUBUĞU
    // ═══════════════════════════════════════════════════════════════════════════

    private void SetStatus(string text) => StatusLabel.Text = text;

    /// <summary>
    /// Kısa süreli bildirim gösterir.
    /// </summary>
    /// <remarks>
    /// Kısayolla yapılan işlemler durum çubuğunda gözden kaçtığı için tuvalin
    /// üzerinde de doğrulanır.
    /// </remarks>
    private void ShowToast(string text)
    {
        SetStatus(text);
        ToastText.Text = text;

        // Süren solma animasyonu temizlenmeli. Aksi hâlde animasyon değeri
        // Opacity'yi ele geçirir ve sonraki atamalar hiçbir etki yapmaz —
        // ilk bildirimden sonrakiler görünmez olur.
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;
        Toast.Visibility = Visibility.Visible;

        _toastTimer ??= CreateToastTimer();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private DispatcherTimer CreateToastTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                FillBehavior = FillBehavior.Stop,
            };

            // Animasyon bitince görünürlüğü kapat; sonraki gösterim temiz başlasın.
            fade.Completed += (_, _) =>
            {
                Toast.BeginAnimation(OpacityProperty, null);
                Toast.Opacity = 0;
                Toast.Visibility = Visibility.Collapsed;
            };

            Toast.BeginAnimation(OpacityProperty, fade);
        };

        return timer;
    }

    private DispatcherTimer? _toastTimer;

    private void UpdateChrome()
    {
        int count = _document.FrameCount;
        var snapshot = _document.Current;

        TitleSummary.Text = $"{_document.Width}×{_document.Height}  ·  {snapshot.TotalDuration.TotalSeconds:0.0} sn";

        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        UndoButton.ToolTip = _history.NextUndo == EditScope.Annotation
            ? "Geri al: çizim (Ctrl+Z)"
            : _history.CanUndo ? $"Geri al: {_document.UndoLabel} (Ctrl+Z)" : "Geri al (Ctrl+Z)";
        RedoButton.ToolTip = _history.CanRedo ? "Yinele (Ctrl+Y)" : "Yinele (Ctrl+Y)";

        bool hasFrames = count > 0;
        DeleteButton.IsEnabled = hasFrames && count > 1;
        DuplicateButton.IsEnabled = hasFrames;
        ReverseButton.IsEnabled = count > 1;
        ExportButton.IsEnabled = hasFrames && !_exporting;

        ClampRange();

        // Aralık tüm kareleri kapsıyorsa göstermenin anlamı yok.
        bool partialRange = count > 0 && (_rangeStart > 0 || _rangeEnd < count - 1);
        RangeLabel.Text = partialRange ? $"{_rangeStart + 1}–{_rangeEnd + 1}" : "";
        TrimButton.Visibility = partialRange ? Visibility.Visible : Visibility.Collapsed;
        RangeResetButton.Visibility = partialRange ? Visibility.Visible : Visibility.Collapsed;

        StatsLabel.Text = count == 0 ? "" : $"{SelectedIndex + 1} / {count}";
        StatsLabel.ToolTip = count == 0
            ? null
            : $"Toplam {snapshot.TotalDuration.TotalSeconds:0.0} sn · ortalama {snapshot.AverageDelay:0} ms · " +
              $"bellek {snapshot.ByteSize / (1024.0 * 1024.0):0.0} MB";

        UpdateEstimate();
        UpdatePanelState();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KLAVYE
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_exporting)
        {
            if (e.Key == Key.Escape) CancelExport();
            e.Handled = true;
            return;
        }

        bool ctrl = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Satır içi düzenleme açıkken pencere kısayolları TextBox'ın odağını
        // veya metnini ele geçirmemeli. Esc mevcut metni onaylayıp kapatır.
        if (IsTextEditing)
        {
            if (key == Key.Escape)
            {
                CommitTextEdit();
                e.Handled = true;
            }

            return;
        }

        // Metin kutusundayken kısayollar araya girmesin.
        if (Keyboard.FocusedElement is TextBox)
            return;

        // Bir çizim nesnesi seçiliyken Sil/oklar/geri al nesneye uygulanmalı,
        // kareye değil. Aksi hâlde kullanıcı nesneyi silmek isterken kare siliniyor.
        if (HandleAnnotationKey(key, ctrl, shift))
        {
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape when _cropping: CancelCrop(); break;
            case Key.Escape: Close(); break;

            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.S when ctrl: _ = ExportAsync(); break;
            case Key.D when ctrl: Edit("Çoğalt", f => FrameOperations.Duplicate(f, SelectedIndexes())); break;
            case Key.A when ctrl: Timeline.SelectAll(); break;

            case Key.X when ctrl && shift: BeginCrop(); break;
            case Key.D0 when ctrl: ZoomToFit(); break;

            case Key.Delete: DeleteSelected(); break;
            case Key.Space: TogglePlayback(); break;

            case Key.Left when alt:
                Edit("Öncekileri sil", f => FrameOperations.RemoveBefore(f, SelectedIndexes().Min()));
                break;
            case Key.Right when alt:
                Edit("Sonrakileri sil", f => FrameOperations.RemoveAfter(f, SelectedIndexes().Max()));
                break;

            case Key.Left when ctrl && alt: MoveSelection(left: true); break;
            case Key.Right when ctrl && alt: MoveSelection(left: false); break;

            case Key.Left: StopPlayback(); StepFrame(ctrl ? -10 : -1); break;
            case Key.Right: StopPlayback(); StepFrame(ctrl ? 10 : 1); break;
            case Key.Home: StopPlayback(); SelectFrame(0); break;
            case Key.End: StopPlayback(); SelectFrame(_document.FrameCount - 1); break;

            case Key.I: MarkRangeStart(); break;
            case Key.O: MarkRangeEnd(); break;

            // Çizim araçları
            case Key.V: SelectTool(EditorTool.Select); break;
            case Key.R: SelectTool(EditorTool.Rectangle); break;
            case Key.E: SelectTool(EditorTool.Ellipse); break;
            case Key.A: SelectTool(EditorTool.Arrow); break;
            case Key.P: SelectTool(EditorTool.Pen); break;
            case Key.T: SelectTool(EditorTool.Text); break;
            case Key.S: SelectTool(EditorTool.Step); break;

            default: return;
        }

        e.Handled = true;
    }
}
