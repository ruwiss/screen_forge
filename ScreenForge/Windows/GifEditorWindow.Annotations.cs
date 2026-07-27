using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScreenForge.Editor;
using ScreenForge.Gif.Editing;
using ScreenForge.Settings;
using SkiaSharp;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

public sealed partial class GifEditorWindow
{
    private const double ClipLaneHeight = 18;

    /// <summary>Şeritteki bir nesne satırı.</summary>
    private sealed class ClipRow : INotifyPropertyChanged
    {
        private bool _selected;

        public required SceneItem Item { get; init; }
        public required ObjectClip Clip { get; init; }

        public string Label => Clip.Name;

        /// <summary>Şerit çubuğunun rengi; nesneyi ayırt etmeye yarar.</summary>
        public Brush Swatch => new SolidColorBrush(Color.FromRgb(Clip.Color.Red, Clip.Color.Green, Clip.Color.Blue));

        public bool Visible
        {
            get => Clip.Visible;
            set
            {
                if (Clip.Visible == value) return;
                Clip.Visible = value;
                Raise(nameof(Visible));
            }
        }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Raise(nameof(Selected)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ObservableCollection<ClipRow> _clipRows = new();
    private readonly ToolStyleMemory _toolStyle;

    private AnnotationTrack _track = null!;
    private InteractiveCanvas? _annotationCanvas;

    // GIF tuvali üzerindeki satır içi metin düzenleme durumu.
    private TextBox? _textBox;
    private TextItem? _editingTextItem;
    private string _textBeforeEdit = "";
    private bool _textEditCommitted;

    private bool IsTextEditing => _editingTextItem != null;

    // Klip sürükleme durumu
    private ClipRow? _dragRow;
    private int _dragGrabFrame;
    private bool _dragStartEdge;
    private bool _dragEndEdge;
    private int _dragOriginStart;
    private int _dragOriginEnd;

    private (ToggleButton Button, EditorTool Tool)[] ToolButtons => new[]
    {
        (ToolSelect, EditorTool.Select),
        (ToolRect, EditorTool.Rectangle),
        (ToolEllipse, EditorTool.Ellipse),
        (ToolArrow, EditorTool.Arrow),
        (ToolPen, EditorTool.Pen),
        (ToolText, EditorTool.Text),
        (ToolStep, EditorTool.Step),
    };

    private void WireAnnotations()
    {
        _track = new AnnotationTrack(new SKSize(_document.Width, _document.Height));

        foreach (var (button, tool) in ToolButtons)
        {
            var captured = tool;
            button.Click += (_, _) => SelectTool(captured);
        }

        ClipRows.ItemsSource = _clipRows;

        ClipCanvas.MouseLeftButtonDown += OnClipCanvasMouseDown;
        ClipCanvas.MouseMove += OnClipCanvasMouseMove;
        ClipCanvas.MouseLeftButtonUp += OnClipCanvasMouseUp;
        ClipCanvas.SizeChanged += (_, _) => RefreshClips();

        // Klip şeridi kare şeridiyle yatay olarak kilitli kaydırılır;
        // böylece bir klip her zaman kapsadığı karelerin altında durur.
        Timeline.Loaded += (_, _) => HookTimelineScroll();

        ClearFrameButton.Click += (_, _) => ClearCurrentFrame();

        // Kare komutları başlangıçta boş kareye göre ayarlanır.
        UpdateClipToolbar();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ARAÇ SEÇİMİ
    // ═══════════════════════════════════════════════════════════════════════════

    private void SelectTool(EditorTool tool)
    {
        foreach (var (button, mapped) in ToolButtons)
            button.IsChecked = mapped == tool;

        EnsureAnnotationCanvas();

        if (_annotationCanvas != null)
        {
            _annotationCanvas.Tool = tool;
            _annotationCanvas.Focus();
        }

        SetStatus(tool switch
        {
            EditorTool.Select => "Seç — nesneyi sürükleyin, kenarlarından boyutlandırın",
            EditorTool.Text => "Metin — eklemek istediğiniz yere tıklayın",
            _ => $"{ToolName(tool)} — sürükleyerek çizin",
        });
    }

    private static string ToolName(EditorTool tool) => tool switch
    {
        EditorTool.Rectangle => "Dikdörtgen",
        EditorTool.Ellipse => "Elips",
        EditorTool.Arrow => "Ok",
        EditorTool.Pen => "Kalem",
        EditorTool.Text => "Metin",
        EditorTool.Step => "Adım",
        EditorTool.Blur => "Bulanıklık",
        _ => "Seç",
    };

    private static string ItemLabel(SceneItem item) => item switch
    {
        RectItem => "Dikdörtgen",
        EllipseItem => "Elips",
        ArrowItem => "Ok",
        LineItem => "Çizgi",
        HighlightItem => "Vurgu",
        FreehandItem => "Çizim",
        TextItem text => string.IsNullOrWhiteSpace(text.Text) ? "Metin" : Shorten(text.Text),
        StepItem step => $"Adım {step.Number}",
        BlurItem => "Bulanıklık",
        ImageItem => "Görsel",
        _ => "Nesne",
    };

    private static string Shorten(string text) => text.Length <= 16 ? text : text[..14] + "…";

    // ═══════════════════════════════════════════════════════════════════════════
    //  TUVAL
    // ═══════════════════════════════════════════════════════════════════════════

    private void EnsureAnnotationCanvas()
    {
        if (_annotationCanvas != null)
            return;

        _annotationCanvas = new InteractiveCanvas(_track.Scene, _toolStyle)
        {
            // Tuval zaten kare boyutunda yerleştirildiği için ölçek uygulanmaz.
            Layout = LayoutMode.OneToOne,
            // Kare görüntüsü altta kalır; tuval yalnızca çizimleri gösterir.
            TransparentBackground = true,
        };

        _annotationCanvas.SelectionChanged += OnAnnotationSelectionChanged;
        _annotationCanvas.ItemMoved += OnAnnotationItemMoved;
        _annotationCanvas.TextEditRequested += BeginTextEdit;
        _track.Scene.Changed += OnAnnotationSceneChanged;

        // Yalnızca geçerli karede duran nesneler seçilebilir olmalı; aksi hâlde
        // o karede görünmeyen bir nesneye tıklanıp özellikleri açılıyor.
        _track.Scene.HitFilter = item => _track.ClipOf(item).CoversFrame(SelectedIndex);

        AnnotationHost.Content = _annotationCanvas;
    }

    /// <summary>
    /// Metni doğrudan GIF tuvali üzerinde düzenler. TextBox şeffaftır; görünür
    /// metin her değişimde TextItem tarafından çizilir, caret ise odakta kalır.
    /// </summary>
    private void BeginTextEdit(TextItem item)
    {
        if (_editingTextItem != null)
        {
            if (ReferenceEquals(_editingTextItem, item))
            {
                _textBox?.Focus();
                return;
            }

            CommitTextEdit();
        }

        _editingTextItem = item;
        _textBeforeEdit = item.Text;
        _textEditCommitted = false;
        item.Measure();

        var box = new TextBox
        {
            Text = ToTextBoxText(item.Text),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 8,
            FontFamily = new System.Windows.Media.FontFamily(item.FontFamily),
            FontSize = Math.Max(8, item.FontSize * _zoom),
            FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal,
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0x2F, 0x6F, 0xED)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = ToWpfTextAlignment(item.TextAlignment),
        };
        _textBox = box;

        PlaceTextBox();
        System.Windows.Controls.Panel.SetZIndex(box, 1000);
        TextEditLayer.Children.Add(box);

        box.TextChanged += (_, _) =>
        {
            if (_editingTextItem == null)
                return;

            _editingTextItem.Text = NormalizeEditorText(box.Text);
            _editingTextItem.Measure();
            _track.Scene.RaiseChanged();
            PlaceTextBox();
        };
        box.LostFocus += (_, _) => CommitTextEdit();
        box.PreviewKeyDown += (_, e) =>
        {
            // Enter satır ekler; Ctrl+Enter düzenlemeyi bitirir.
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                CommitTextEdit();
            }
        };

        // The original click targets InteractiveCanvas. Defer focus until that
        // mouse route has completed so the canvas cannot reclaim it and trigger
        // LostFocus, which would otherwise discard the empty TextItem.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (ReferenceEquals(_textBox, box) && ReferenceEquals(_editingTextItem, item))
            {
                box.Focus();
                box.SelectAll();
            }
        }));
    }

    private void PlaceTextBox()
    {
        if (_textBox == null || _editingTextItem == null)
            return;

        var size = _editingTextItem.Measure();
        _textBox.Width = Math.Max(8, size.Width * _zoom + 4);
        _textBox.Height = Math.Max(16, size.Height * _zoom + 4);
        Canvas.SetLeft(_textBox, _editingTextItem.Position.X * _zoom);
        Canvas.SetTop(_textBox, _editingTextItem.Position.Y * _zoom);
    }

    private static string NormalizeEditorText(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string ToTextBoxText(string text)
        => NormalizeEditorText(text).Replace("\n", Environment.NewLine);

    private static TextAlignment ToWpfTextAlignment(TextAlignmentMode alignment) => alignment switch
    {
        TextAlignmentMode.Center => TextAlignment.Center,
        TextAlignmentMode.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private void CommitTextEdit()
    {
        if (_textEditCommitted || _editingTextItem == null)
            return;

        _textEditCommitted = true;
        var item = _editingTextItem;
        var box = _textBox;
        item.Text = NormalizeEditorText(box?.Text ?? item.Text);

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            _track.Scene.Apply(new RemoveItemAction(item));
        }
        else if (item.Text != _textBeforeEdit)
        {
            var after = (TextItem)item.Clone();
            var before = (TextItem)item.Clone();
            before.Text = _textBeforeEdit;
            _track.Scene.Apply(new ModifyItemAction(item, before, after));
        }

        if (box != null)
            TextEditLayer.Children.Remove(box);

        _textBox = null;
        _editingTextItem = null;
        _track.Scene.RaiseChanged();
        _annotationCanvas?.Focus();
    }

    private void OnAnnotationSceneChanged()
    {
        RegisterNewItems();
        RebuildClipRows();
        UpdatePreview();

        // Sahnenin kendi yığınına yazılan her işlem ortak sıraya da kaydedilir;
        // böylece Ctrl+Z kare ve çizim arasında doğru sırayla ilerler.
        if (!_suppressHistorySync)
            SyncAnnotationHistory();
    }

    /// <summary>Sahne yığınındaki yeni işlemleri ortak sıraya aktarır.</summary>
    private void SyncAnnotationHistory()
    {
        int depth = _track.Scene.UndoDepth;

        while (_annotationDepth < depth)
        {
            _history.Record(EditScope.Annotation);
            _annotationDepth++;
        }

        // Geri alma sahne tarafında gerçekleştiyse sayaç da gerilemeli.
        if (_annotationDepth > depth)
            _annotationDepth = depth;

        UpdateChrome();
    }

    /// <summary>Ortak sıradan gelen çizim geri alma isteği.</summary>
    private void UndoAnnotation()
    {
        if (!_track.Scene.CanUndo)
            return;

        _suppressHistorySync = true;
        _track.Scene.Undo();
        _suppressHistorySync = false;

        _annotationDepth = _track.Scene.UndoDepth;
        RebuildClipRows();
        UpdatePreview();
    }

    /// <summary>Ortak sıradan gelen çizim yineleme isteği.</summary>
    private void RedoAnnotation()
    {
        if (!_track.Scene.CanRedo)
            return;

        _suppressHistorySync = true;
        _track.Scene.Redo();
        _suppressHistorySync = false;

        _annotationDepth = _track.Scene.UndoDepth;
        RebuildClipRows();
        UpdatePreview();
    }

    /// <summary>Ortak sıraya aktarılmış sahne işlemlerinin sayısı.</summary>
    private int _annotationDepth;

    private bool _suppressHistorySync;

    /// <summary>
    /// Yeni çizilen nesneleri kaydeder.
    /// </summary>
    /// <remarks>
    /// Nesne yalnızca çizildiği karede görünür. Diğer karelere yaymak
    /// kullanıcının açık tercihidir; şeritten kenarı çekerek ya da
    /// "Tümüne uygula" ile yapılır.
    /// </remarks>
    private void RegisterNewItems()
    {
        foreach (var item in _track.Scene.Items)
        {
            if (_track.IsRegistered(item))
                continue;

            _track.Register(item, SelectedIndex, SelectedIndex, ItemLabel(item));
        }
    }

    private void OnAnnotationSelectionChanged()
    {
        SyncRowSelection();
        UpdateClipToolbar();
        RefreshClips();
        RebuildObjectPanel();
    }

    /// <summary>
    /// Nesne taşındığında geçerli kareye konumunu yazar.
    /// </summary>
    /// <remarks>
    /// Nesne birden çok kareyi kapsıyorsa taşıma o kareye anahtar bırakır ve
    /// aradaki kareler yumuşak geçişe döner. Tek karelik nesnede anahtar
    /// gereksizdir; taşıma doğrudan geometriye yazılmıştır.
    /// </remarks>
    private void OnAnnotationItemMoved()
    {
        var canvas = _annotationCanvas;
        if (canvas == null)
            return;

        foreach (var item in canvas.Selection)
        {
            var clip = _track.ClipOf(item);

            if (clip.Length > 1)
                clip.SetOffsetAt(SelectedIndex, clip.OffsetAt(SelectedIndex));
        }

        RefreshClips();
        UpdateClipToolbar();
        UpdatePreview();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ŞERİT
    // ═══════════════════════════════════════════════════════════════════════════

    private void RebuildClipRows()
    {
        _clipRows.Clear();

        foreach (var item in _track.Scene.Items)
        {
            _clipRows.Add(new ClipRow
            {
                Item = item,
                Clip = _track.ClipOf(item),
            });
        }

        ClipStrip.Visibility = _clipRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncRowSelection();
        UpdateClipToolbar();
        RefreshClips();
    }

    private void SyncRowSelection()
    {
        var selection = _annotationCanvas?.Selection;

        foreach (var row in _clipRows)
            row.Selected = selection?.Contains(row.Item) == true;
    }

    private void ClipRowSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ClipRow row })
            _annotationCanvas?.SetSelection(row.Item);
    }

    /// <summary>
    /// Nesne kliplerini kare şeridiyle hizalı olarak çizer.
    /// </summary>
    /// <remarks>
    /// Klip tuvali kare şeridiyle aynı yatay ölçeği ve kaydırma konumunu
    /// paylaşır; böylece bir klibin hangi karelere denk geldiği doğrudan
    /// yukarıdaki küçük resimlerden okunur.
    /// </remarks>
    private void RefreshClips()
    {
        ClipCanvas.Children.Clear();
        _barLookup.Clear();

        int count = _document.FrameCount;
        if (count == 0 || _clipRows.Count == 0)
            return;

        double perFrame = FrameSlotWidth;
        ClipCanvas.Width = count * perFrame;
        ClipCanvas.Height = Math.Max(ClipLaneHeight, _clipRows.Count * ClipLaneHeight);

        // Geçerli kare vurgusu — yukarıdaki seçili küçük resimle hizalı.
        var playhead = new WpfRectangle
        {
            Width = perFrame,
            Height = ClipCanvas.Height,
            Fill = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(playhead, SelectedIndex * perFrame);
        ClipCanvas.Children.Add(playhead);

        for (int i = 0; i < _clipRows.Count; i++)
        {
            var row = _clipRows[i];
            row.Clip.Clamp(count);

            double top = i * ClipLaneHeight;
            double left = row.Clip.StartFrame * perFrame;
            double width = Math.Max(perFrame * 0.6, row.Clip.Length * perFrame);

            var color = row.Clip.Color;
            var fill = row.Visible
                ? Color.FromArgb((byte)(row.Selected ? 245 : 165), color.Red, color.Green, color.Blue)
                : Color.FromArgb(45, 0x9A, 0xA4, 0xB8);

            var bar = new Border
            {
                Width = width,
                Height = ClipLaneHeight - 5,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(fill),
                BorderBrush = row.Selected ? Brushes.White : Brushes.Transparent,
                BorderThickness = new Thickness(row.Selected ? 1 : 0),
                Cursor = Cursors.SizeAll,
                Tag = row,
                ToolTip = row.Clip.Length == 1
                    ? $"{row.Label} · yalnızca kare {row.Clip.StartFrame + 1}\nKenarından çekerek uzatın"
                    : $"{row.Label} · kare {row.Clip.StartFrame + 1}–{row.Clip.EndFrame + 1} ({row.Clip.Length} kare)\n" +
                      "Ortadan sürükle: kaydır · kenarlardan: uzat/kısalt",
            };

            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, top + 2.5);
            ClipCanvas.Children.Add(bar);
            _barLookup.Add(bar);

            // Kenar tutamakları: hem imleci değiştirir hem çekilebilir olduğunu
            // gösterir. Hit-test açık olmalı, aksi hâlde imleç hover'da dönmez.
            double grip = Math.Min(9, width / 3);
            double barHeight = ClipLaneHeight - 5;

            foreach (bool leading in new[] { true, false })
            {
                var handle = new Border
                {
                    Width = grip,
                    Height = barHeight,
                    Cursor = Cursors.SizeWE,
                    Tag = row,
                    Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    CornerRadius = leading
                        ? new CornerRadius(2, 0, 0, 2)
                        : new CornerRadius(0, 2, 2, 0),
                    ToolTip = leading ? "Başlangıcı çek" : "Bitişi çek",
                };

                Canvas.SetLeft(handle, leading ? left : left + width - grip);
                Canvas.SetTop(handle, top + 2.5);
                ClipCanvas.Children.Add(handle);
            }

            // Konum anahtarları: nesnenin durak noktaları
            foreach (var key in row.Clip.Keys)
            {
                var marker = new WpfRectangle
                {
                    Width = 6,
                    Height = 6,
                    Fill = key.Frame == SelectedIndex ? Brushes.White : new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(45),
                    IsHitTestVisible = false,
                    ToolTip = $"Konum · kare {key.Frame + 1}",
                };

                Canvas.SetLeft(marker, key.Frame * perFrame + perFrame / 2 - 3);
                Canvas.SetTop(marker, top + ClipLaneHeight / 2 - 3);
                ClipCanvas.Children.Add(marker);
            }
        }
    }

    /// <summary>
    /// Kare şeridindeki bir kutunun genişliği; klipler buna hizalanır.
    /// </summary>
    /// <remarks>
    /// Değer sabit varsayılmaz, gerçek kapsayıcıdan ölçülür. Kenar boşlukları
    /// ya da şablon değişince hizalama kendiliğinden doğru kalır.
    /// </remarks>
    private double FrameSlotWidth
    {
        get
        {
            if (_frameSlotWidth > 0)
                return _frameSlotWidth;

            if (Timeline.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement first)
            {
                double measured = first.ActualWidth + first.Margin.Left + first.Margin.Right;
                if (measured > 1)
                {
                    _frameSlotWidth = measured;
                    return measured;
                }
            }

            // Şablon henüz oluşmadıysa geçici tahmin.
            return 90;
        }
    }

    private double _frameSlotWidth;

    /// <summary>Kare şeridinin yatay kaydırmasını klip şeridine bağlar.</summary>
    private void HookTimelineScroll()
    {
        var source = FindScrollViewer(Timeline);
        if (source == null)
            return;

        source.ScrollChanged += (_, e) =>
        {
            if (Math.Abs(e.HorizontalChange) > 0.01 || Math.Abs(e.HorizontalOffset - ClipScroll.HorizontalOffset) > 0.5)
                ClipScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        };

        ClipScroll.ScrollChanged += (_, e) =>
        {
            if (Math.Abs(e.HorizontalChange) > 0.01)
                source.ScrollToHorizontalOffset(e.HorizontalOffset);
        };
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
            return viewer;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }

        return null;
    }

    // ─── Şerit etkileşimi ─────────────────────────────────────────────────────

    private void OnClipCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ClipCanvas);
        int frame = FrameAt(position.X);

        var hit = FindClipBar(position);
        if (hit == null)
        {
            SelectFrame(frame);
            return;
        }

        var (row, bar) = hit.Value;
        _annotationCanvas?.SetSelection(row.Item);
        SelectFrame(frame);

        double left = Canvas.GetLeft(bar);
        double grip = Math.Min(10, bar.Width / 3);

        _dragRow = row;
        _dragStartEdge = position.X - left < grip;
        _dragEndEdge = left + bar.Width - position.X < grip;
        _dragGrabFrame = frame;
        _dragOriginStart = row.Clip.StartFrame;
        _dragOriginEnd = row.Clip.EndFrame;

        ClipCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnClipCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ClipCanvas);

        if (_dragRow == null)
        {
            var hover = FindClipBar(position);
            ClipCanvas.Cursor = hover == null ? Cursors.Arrow
                : IsNearEdge(position, hover.Value.Bar) ? Cursors.SizeWE : Cursors.SizeAll;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        // Sürükleme boyunca imleç kilitli kalsın; kenar mı taşıma mı belli olsun.
        ClipCanvas.Cursor = _dragStartEdge || _dragEndEdge ? Cursors.SizeWE : Cursors.SizeAll;

        int last = Math.Max(0, _document.FrameCount - 1);
        int frame = Math.Clamp(FrameAt(position.X), 0, last);
        var clip = _dragRow.Clip;

        if (_dragStartEdge)
        {
            clip.StartFrame = Math.Min(frame, clip.EndFrame);
        }
        else if (_dragEndEdge)
        {
            clip.EndFrame = Math.Max(frame, clip.StartFrame);
        }
        else
        {
            // Tüm aralığı kaydır; uzunluk korunur.
            int delta = frame - _dragGrabFrame;
            int length = _dragOriginEnd - _dragOriginStart;
            int start = Math.Clamp(_dragOriginStart + delta, 0, Math.Max(0, last - length));

            clip.StartFrame = start;
            clip.EndFrame = Math.Min(last, start + length);
        }

        RefreshClips();
        UpdateClipToolbar();
        UpdatePreview();
    }

    private void OnClipCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragRow == null)
            return;

        var clip = _dragRow.Clip;
        SetStatus(clip.Length == 1
            ? $"{_dragRow.Label} · kare {clip.StartFrame + 1}"
            : $"{_dragRow.Label} · kare {clip.StartFrame + 1}–{clip.EndFrame + 1} ({clip.Length} kare)");

        _dragRow = null;
        ClipCanvas.Cursor = Cursors.Arrow;
        ClipCanvas.ReleaseMouseCapture();
    }

    /// <summary>
    /// Verilen konumdaki klip çubuğunu bulur.
    /// </summary>
    /// <remarks>
    /// Yalnızca ana çubuk döndürülür; kenar tutamakları çubuğun üzerinde
    /// durduğu için ayrıca aranmaz. Tutamak sayesinde imleç doğru dönerken
    /// isabet hesabı tek yerden yapılır.
    /// </remarks>
    private (ClipRow Row, Border Bar)? FindClipBar(Point position)
    {
        foreach (var child in ClipCanvas.Children)
        {
            // Tutamaklar da ClipRow etiketli; yalnızca gerçek çubukları al.
            if (child is not Border { Tag: ClipRow row } bar || !ReferenceEquals(bar.Tag, row) || bar.Width < 6)
                continue;

            if (!_barLookup.Contains(bar))
                continue;

            double left = Canvas.GetLeft(bar);
            double top = Canvas.GetTop(bar);

            if (position.X >= left && position.X <= left + bar.Width &&
                position.Y >= top && position.Y <= top + bar.Height)
            {
                return (row, bar);
            }
        }

        return null;
    }

    /// <summary>Gerçek klip çubukları; kenar tutamaklarından ayırmaya yarar.</summary>
    private readonly HashSet<Border> _barLookup = new();

    private static bool IsNearEdge(Point position, Border bar)
    {
        double left = Canvas.GetLeft(bar);
        double grip = Math.Min(9, bar.Width / 3);
        return position.X - left < grip || left + bar.Width - position.X < grip;
    }

    private int FrameAt(double x)
    {
        int count = _document.FrameCount;
        return count == 0 ? 0 : Math.Clamp((int)(x / FrameSlotWidth), 0, count - 1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KARE KOMUTLARI
    // ═══════════════════════════════════════════════════════════════════════════

    private void UpdateClipToolbar()
    {
        int count = _track.ItemsAt(SelectedIndex).Count;

        ClearFrameButton.IsEnabled = count > 0;

        FrameObjectsLabel.Text = count == 0
            ? "nesne yok"
            : count == 1 ? "1 nesne" : $"{count} nesne";
    }

    /// <summary>Geçerli karedeki nesneleri temizler.</summary>
    private void ClearCurrentFrame()
    {
        int changed = _track.ClearFrame(SelectedIndex);

        RebuildClipRows();
        UpdatePreview();

        ShowToast(changed == 0
            ? "Bu karede nesne yok"
            : $"Kare {SelectedIndex + 1}: {changed} nesne temizlendi");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PANO
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Kopyalanan nesneler ve klipleri.</summary>
    private readonly List<(SceneItem Item, ObjectClip Clip)> _clipboard = new();

    /// <summary>Seçili nesneleri panoya alır.</summary>
    private bool TryCopyAnnotationSelection()
    {
        if (_annotationCanvas is not { } canvas || canvas.Selection.Count == 0)
            return false;

        _clipboard.Clear();

        foreach (var item in SceneClipboard.OrderBySceneZ(_track.Scene, canvas.Selection))
            _clipboard.Add((item.Clone(), _track.ClipOf(item).Clone()));

        ShowToast(_clipboard.Count == 1 ? "Nesne kopyalandı" : $"{_clipboard.Count} nesne kopyalandı");
        return true;
    }

    /// <summary>
    /// Panodaki nesneleri geçerli kareye yapıştırır.
    /// </summary>
    /// <remarks>
    /// Klip uzunluğu korunur ama geçerli kareye taşınır; böylece aynı çizimi
    /// başka bir bölüme kolayca uygulanır.
    /// </remarks>
    private bool TryPasteAnnotation()
    {
        if (_clipboard.Count == 0 || _annotationCanvas is not { } canvas)
            return false;

        int last = Math.Max(0, _document.FrameCount - 1);
        var pasted = new List<SceneItem>();

        foreach (var (source, clip) in _clipboard)
        {
            var copy = source.Clone();
            _track.Scene.Items.Add(copy);

            var newClip = _track.RegisterCopy(copy, clip, ItemLabel(copy));

            // Klibi geçerli kareye taşı, uzunluğu koru.
            int length = clip.Length - 1;
            newClip.StartFrame = Math.Clamp(SelectedIndex, 0, Math.Max(0, last - length));
            newClip.EndFrame = Math.Min(last, newClip.StartFrame + length);

            pasted.Add(copy);
        }

        canvas.SetSelection(pasted);
        RebuildClipRows();
        UpdatePreview();
        ShowToast(pasted.Count == 1 ? "Nesne yapıştırıldı" : $"{pasted.Count} nesne yapıştırıldı");
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NESNE KOMUTLARI
    // ═══════════════════════════════════════════════════════════════════════════

    private bool TryDeleteAnnotationSelection()
    {
        if (_annotationCanvas is not { } canvas || canvas.Selection.Count == 0)
            return false;

        int count = canvas.Selection.Count;
        canvas.DeleteSelected();
        RebuildClipRows();
        UpdatePreview();
        ShowToast(count == 1 ? "Nesne silindi" : $"{count} nesne silindi");
        return true;
    }

    private bool TryDuplicateAnnotationSelection()
    {
        if (_annotationCanvas is not { } canvas || canvas.Selection.Count == 0)
            return false;

        var copies = new List<SceneItem>();

        foreach (var item in SceneClipboard.OrderBySceneZ(_track.Scene, canvas.Selection))
        {
            var copy = item.Clone();
            copy.Move(SceneClipboard.DuplicateOffset, SceneClipboard.DuplicateOffset);

            // Klip de kopyalanır; kopya aynı karelerde görünür, adı yenilenir.
            _track.Scene.Items.Add(copy);
            _track.RegisterCopy(copy, _track.ClipOf(item), ItemLabel(copy));
            copies.Add(copy);
        }

        canvas.SetSelection(copies);
        RebuildClipRows();
        UpdatePreview();
        ShowToast(copies.Count == 1 ? "Nesne çoğaltıldı" : $"{copies.Count} nesne çoğaltıldı");
        return true;
    }

    /// <summary>Çizim nesnesi seçiliyken ilgili tuşları nesneye yönlendirir.</summary>
    private bool HandleAnnotationKey(Key key, bool ctrl, bool shift)
    {
        var canvas = _annotationCanvas;
        if (canvas == null)
            return false;

        // Yapıştırma seçim gerektirmez.
        if (key == Key.V && ctrl && _clipboard.Count > 0)
            return TryPasteAnnotation();

        if (canvas.Selection.Count == 0)
            return false;

        switch (key)
        {
            case Key.Delete or Key.Back:
                return TryDeleteAnnotationSelection();

            case Key.C when ctrl:
                return TryCopyAnnotationSelection();

            case Key.X when ctrl:
                // Kes: kopyala, sonra sil.
                TryCopyAnnotationSelection();
                return TryDeleteAnnotationSelection();

            case Key.D when ctrl:
                return TryDuplicateAnnotationSelection();

            case Key.Escape:
                canvas.ClearSelection();
                return true;

            case Key.Left or Key.Right or Key.Up or Key.Down when !ctrl:
            {
                float step = shift ? 10 : 1;
                float dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                float dy = key == Key.Up ? -step : key == Key.Down ? step : 0;

                canvas.NudgeSelection(dx, dy);
                OnAnnotationItemMoved();
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KOMPOZİT
    // ═══════════════════════════════════════════════════════════════════════════

    private void ShiftTracks(int at, int delta)
    {
        if (delta == 0)
            return;

        _track.ShiftForFrameChange(at, delta);
        _track.Clamp(_document.FrameCount);
        RefreshClips();
    }

    /// <summary>
    /// Çoğaltılan her kare için ekleme, kaynağın hemen sonrasında uygulanır.
    /// Böylece kaynak karedeki anahtar yerinde kalır; yalnızca ekleme
    /// noktasının sağındaki anahtarlar yeni konumlarına kayar.
    /// </summary>
    private void ShiftTracksForDuplicatedFrames(IReadOnlyList<int> sourceFrames)
    {
        int inserted = 0;

        foreach (int source in sourceFrames.Distinct().OrderBy(frame => frame))
        {
            int insertionPoint = source + inserted + 1;
            _track.ShiftForFrameChange(insertionPoint, 1);
            inserted++;
        }

        if (inserted == 0)
            return;

        _track.Clamp(_document.FrameCount);
        RefreshClips();
    }

    /// <summary>
    /// Kare çoğaltıldığında o karedeki çizimleri de kopyaya taşır.
    /// </summary>
    /// <remarks>
    /// Kaydırma tek başına yetmez: çoğaltılan kare kaynağın çizimlerini
    /// göstermelidir. Kaynağı tek kare kaplayan nesneler kopyaya genişletilir,
    /// zaten aralığı kapsayanlara dokunulmaz.
    /// </remarks>
    private void ExtendClipsForDuplicatedFrames(IReadOnlyList<int> sourceFrames)
    {
        if (sourceFrames.Count == 0)
            return;

        int last = Math.Max(0, _document.FrameCount - 1);

        // Kareler artan sırada çoğaltıldığı için her kopya kaynağın hemen ardındadır.
        for (int i = 0; i < sourceFrames.Count; i++)
        {
            int source = sourceFrames[i] + i;
            int copy = Math.Min(last, source + 1);

            foreach (var item in _track.Scene.Items)
            {
                var clip = _track.ClipOf(item);

                // Kaynak kareyi kapsıyor ama kopyayı kapsamıyorsa uzat.
                if (clip.CoversFrame(source) && !clip.CoversFrame(copy))
                    clip.EndFrame = Math.Max(clip.EndFrame, copy);
            }
        }

        _track.Clamp(_document.FrameCount);
        RefreshClips();
        UpdateClipToolbar();
    }

    private void LayoutAnnotationCanvas(double width, double height)
    {
        if (_annotationCanvas == null)
            return;

        _annotationCanvas.Width = width;
        _annotationCanvas.Height = height;
    }

    /// <summary>
    /// Önizleme için kareyi hazırlar.
    /// </summary>
    /// <remarks>
    /// Seçili nesneler <see cref="InteractiveCanvas"/> üzerinde canlı çizildiği
    /// için kompozite dahil edilmez; aksi hâlde çift görünürler.
    /// </remarks>
    private byte[] ComposePreviewPixels(byte[] pixels, int frameIndex)
    {
        // Çizim tuvali geçerli karedeki tüm nesneleri zaten canlı gösteriyor;
        // kompozit yalnızca dışa aktarım içindir. Burada tekrar çizmek
        // nesneleri üst üste bindirir.
        return pixels;
    }

    /// <summary>
    /// Çizim tuvalinin görünürlüğünü geçerli kareye göre ayarlar.
    /// </summary>
    /// <remarks>
    /// Seçili nesneler yalnızca kendi kare aralıklarında canlı gösterilir;
    /// aralık dışında tuval boşaltılır ki nesne olmadığı karede görünmesin.
    /// </remarks>
    private void UpdateAnnotationVisibility()
    {
        if (_annotationCanvas == null)
            return;

        // Süzgeç kareye bağlı olduğu için kare değişince yeniden çizilmeli.
        _annotationCanvas.InvalidateVisual();

        // O karede görünmeyen bir nesne seçili kalmamalı; özellikleri de kapanmalı.
        var stale = _annotationCanvas.Selection
            .Where(i => !_track.ClipOf(i).CoversFrame(SelectedIndex))
            .ToList();

        if (stale.Count > 0)
            _annotationCanvas.ClearSelection();
    }

    /// <summary>Dışa aktarım için tüm görünür nesneleri karelere işler.</summary>
    private List<byte[]> ComposeForExport(List<byte[]> frames, int rangeStart,
        int outputWidth, int outputHeight, CancellationToken token)
    {
        if (!AnnotationCompositor.HasWork(_track))
            return frames;

        var result = new List<byte[]>(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            result.Add(AnnotationCompositor.Apply(frames[i], outputWidth, outputHeight,
                _track, rangeStart + i, _document.Width, _document.Height));
        }

        return result;
    }
}
