using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ScreenForge.Gif;
using ScreenForge.Editor;
using ScreenForge.Gif.Editing;
using SkiaSharp;
using ScreenForge.Windows;

namespace ScreenForge.Tests;

/// <summary>
/// Düzenleyici penceresinin gerçekten kurulabildiğini doğrular.
/// XAML kaynak başvuruları ve stil adları yalnızca çalışma anında çözüldüğü
/// için derleme başarısı tek başına yeterli değildir.
/// </summary>
[Collection("WPF")]
public sealed class GifEditorWindowTests
{
    [Fact]
    public void Constructor_BuildsWindowWithFrames()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 4);

            var window = new GifEditorWindow(recorder);

            Assert.Equal(4, GetTimeline(window).Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void Constructor_HandlesEmptyRecording()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 0);

            // Kare yakalanamadan durdurulan kayıt pencereyi çökertmemeli.
            var window = new GifEditorWindow(recorder);

            Assert.Empty(GetTimeline(window).Items);
            window.Close();
        });
    }

    [Fact]
    public void ToolbarButtons_ResolveStylesAndStartEnabled()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 3);
            var window = new GifEditorWindow(recorder);

            // Geçmiş boşken geri alma kapalı, dışa aktarma açık olmalı.
            Assert.False(Find<Button>(window, "UndoButton").IsEnabled);
            Assert.False(Find<Button>(window, "RedoButton").IsEnabled);
            Assert.True(Find<Button>(window, "ExportButton").IsEnabled);
            Assert.True(Find<Button>(window, "DeleteButton").IsEnabled);

            window.Close();
        });
    }

    [Fact]
    public void Window_StartsMaximized()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 3);
            var window = new GifEditorWindow(recorder);

            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Close();
        });
    }

    [Fact]
    public void ToolbarIcons_AllResolve()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 2);
            var window = new GifEditorWindow(recorder);

            // Eksik bir ikon anahtarı yalnızca çalışma anında ortaya çıkar.
            foreach (string name in new[]
            {
                "IconUndo", "IconRedo", "IconTrash", "IconDeleteBefore", "IconDeleteAfter",
                "IconDuplicate", "IconReverse", "IconMoveLeft", "IconMoveRight",
                "IconRotateLeft", "IconRotateRight",
                "IconCrop", "IconPlay", "IconPause", "IconPrevFrame", "IconNextFrame",
                "IconFirstFrame", "IconLastFrame", "IconMarkIn", "IconMarkOut", "IconReset", "IconFit",
            })
            {
                Assert.True(window.TryFindResource(name) is Geometry, $"{name} çözülemedi");
            }

            window.Close();
        });
    }

    [Fact]
    public void SidePanels_ArePopulated()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 3);
            var window = new GifEditorWindow(recorder);

            // Üç panel de denetim üretmiş olmalı.
            Assert.NotEmpty(Find<StackPanel>(window, "ExportPanel").Children);
            Assert.NotEmpty(Find<StackPanel>(window, "FramePanel").Children);
            Assert.NotEmpty(Find<StackPanel>(window, "OverlayPanel").Children);

            window.Close();
        });
    }

    [Fact]
    public void Delete_RemovesFrameAndEnablesUndo()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 4);
            var window = new GifEditorWindow(recorder);

            var timeline = GetTimeline(window);
            timeline.SelectedIndex = 1;

            Find<Button>(window, "DeleteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(3, timeline.Items.Count);
            Assert.True(Find<Button>(window, "UndoButton").IsEnabled);

            window.Close();
        });
    }

    [Fact]
    public void Undo_RestoresDeletedFrame()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 4);
            var window = new GifEditorWindow(recorder);

            var timeline = GetTimeline(window);
            timeline.SelectedIndex = 0;

            Find<Button>(window, "DeleteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(3, timeline.Items.Count);

            Find<Button>(window, "UndoButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(4, timeline.Items.Count);

            window.Close();
        });
    }

    [Fact]
    public void Reverse_KeepsFrameCount()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 5);
            var window = new GifEditorWindow(recorder);

            Find<Button>(window, "ReverseButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(5, GetTimeline(window).Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void Rotate_SwapsCanvasDimensions()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 2, width: 16, height: 8);
            var window = new GifEditorWindow(recorder);

            Find<Button>(window, "RotateRightButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Döndürme arka planda çalışır; tamamlanmasını bekle.
            WpfRunner.DrainUntil(() => Find<TextBlock>(window, "TitleSummary").Text.Contains("8×16"));

            Assert.Contains("8×16", Find<TextBlock>(window, "TitleSummary").Text);
            window.Close();
        });
    }

    [Fact]
    public void HeavyOperations_DoNotBlockUiThread()
    {
        WpfRunner.Run(() =>
        {
            // Ağır iş: 40 kare × 640×480. Arayüz iş parçacığında yapılsaydı
            // düğmeye basıldığı anda kilitlenirdi.
            using var recorder = MakeRecorder(frameCount: 40, width: 640, height: 480);
            var window = new GifEditorWindow(recorder);

            var clock = Stopwatch.StartNew();
            Find<Button>(window, "RotateRightButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            clock.Stop();

            // Tıklama işleyicisi hemen dönmeli; iş arka planda sürüyor.
            Assert.True(clock.ElapsedMilliseconds < 250,
                $"tıklama arayüzü {clock.ElapsedMilliseconds} ms bloke etti");

            WpfRunner.DrainUntil(() => Find<TextBlock>(window, "TitleSummary").Text.Contains("480×640"));
            window.Close();
        });
    }

    [Fact]
    public void ReorderOperations_AreFastOnLargeRecordings()
    {
        WpfRunner.Run(() =>
        {
            // Sıralama işlemleri piksele dokunmaz; küçük resimler önbellekten
            // geldiği için kare sayısından bağımsız olarak hızlı olmalı.
            using var recorder = MakeRecorder(frameCount: 60, width: 640, height: 480);
            var window = new GifEditorWindow(recorder);

            var clock = Stopwatch.StartNew();
            Find<Button>(window, "ReverseButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            clock.Stop();

            Assert.True(clock.ElapsedMilliseconds < 300,
                $"ters çevirme {clock.ElapsedMilliseconds} ms sürdü");

            window.Close();
        });
    }

    [Fact]
    public void ObjectStrip_AppearsWhenObjectDrawn()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            // Nesne yokken şerit gizli.
            var strip = Find<Border>(window, "ClipStrip");
            Assert.Equal(Visibility.Collapsed, strip.Visibility);

            AddObject(window, new RectItem { Bounds = new SKRect(4, 4, 40, 40) });

            Assert.Equal(Visibility.Visible, strip.Visibility);
            Assert.NotEmpty(Find<ItemsControl>(window, "ClipRows").Items);

            window.Close();
        });
    }

    [Fact]
    public void EachObject_GetsItsOwnTimelineRow()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 30);
            var window = new GifEditorWindow(recorder);

            AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            AddObject(window, new EllipseItem { Bounds = new SKRect(30, 30, 50, 50) });

            // Her nesne şeritte ayrı satır alır.
            Assert.Equal(2, Find<ItemsControl>(window, "ClipRows").Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void FrameCommands_ReflectObjectsOnCurrentFrame()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            var clearFrame = Find<Button>(window, "ClearFrameButton");
            var extendClip = Find<Button>(window, "ExtendClipButton");

            // Boş karede kare komutları kapalı.
            Assert.False(clearFrame.IsEnabled);
            Assert.False(extendClip.IsEnabled);

            AddObject(window, new RectItem { Bounds = new SKRect(4, 4, 40, 40) });

            Assert.True(clearFrame.IsEnabled);
            Assert.False(extendClip.IsEnabled);

            window.Close();
        });
    }

    [Fact]
    public void AnnotationToolButtons_AreMutuallyExclusive()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 10);
            var window = new GifEditorWindow(recorder);

            Find<ToggleButton>(window, "ToolArrow").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(Find<ToggleButton>(window, "ToolArrow").IsChecked);
            Assert.False(Find<ToggleButton>(window, "ToolSelect").IsChecked);
            Assert.False(Find<ToggleButton>(window, "ToolRect").IsChecked);

            window.Close();
        });
    }

    [Fact]
    public void AnnotationCanvas_DoesNotHideFrameBehindIt()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 10);
            var window = new GifEditorWindow(recorder);

            Find<ToggleButton>(window, "ToolRect").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Çizim yüzeyi opak zemin çizerse kare tamamen kaybolur.
            var canvas = GetAnnotationCanvas(window);

            Assert.True(canvas.TransparentBackground,
                "çizim tuvali saydam olmalı, aksi hâlde kareyi örter");

            window.Close();
        });
    }

    [Fact]
    public void Delete_RemovesSelectedObjectInsteadOfFrame()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 12);
            var window = new GifEditorWindow(recorder);
            var frames = GetTimeline(window);

            var item = AddObject(window, new RectItem { Bounds = new SKRect(4, 4, 40, 40) });
            GetAnnotationCanvas(window).SetSelection(item);

            int framesBefore = frames.Items.Count;
            Find<Button>(window, "DeleteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Nesne gitmeli, kare sayısı korunmalı.
            Assert.Empty(GetTrack(window).Scene.Items);
            Assert.Equal(framesBefore, frames.Items.Count);

            window.Close();
        });
    }

    [Fact]
    public void Delete_RemovesOnlyTheSelectedObject()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 12);
            var window = new GifEditorWindow(recorder);

            var first = AddObject(window, new RectItem { Bounds = new SKRect(4, 4, 40, 40) });
            var second = AddObject(window, new EllipseItem { Bounds = new SKRect(50, 50, 80, 80) });
            GetAnnotationCanvas(window).SetSelection(first);

            Find<Button>(window, "DeleteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var items = GetTrack(window).Scene.Items;
            Assert.DoesNotContain(first, items);
            Assert.Contains(second, items);

            window.Close();
        });
    }

    [Fact]
    public void TitleBar_UsesAppLogo()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 2);
            var window = new GifEditorWindow(recorder);

            var logo = Find<System.Windows.Controls.Image>(window, "TitleLogo");
            Assert.Equal(18, logo.Width);
            Assert.Equal(18, logo.Height);

            window.Close();
        });
    }

    [Fact]
    public void ClipStrip_DoesNotShowFrameObjectCount()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 4);
            var window = new GifEditorWindow(recorder);

            Assert.Null(window.FindName("FrameObjectsLabel"));
            Assert.Equal(78, window.TimelineItemWidth);

            window.Close();
        });
    }

    [Fact]
    public void Delete_RemovesFrameWhenNoObjectSelected()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 12);
            var window = new GifEditorWindow(recorder);
            var timeline = GetTimeline(window);

            int framesBefore = timeline.Items.Count;
            Find<Button>(window, "DeleteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Çizim seçimi yokken eski davranış sürmeli.
            Assert.Equal(framesBefore - 1, timeline.Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void Duplicate_CopiesSelectedObjectInsteadOfFrame()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 12);
            var window = new GifEditorWindow(recorder);
            var frames = GetTimeline(window);

            var item = AddObject(window, new RectItem { Bounds = new SKRect(4, 4, 40, 40) });
            GetAnnotationCanvas(window).SetSelection(item);

            int framesBefore = frames.Items.Count;
            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(2, GetTrack(window).Scene.Items.Count);
            Assert.Equal(framesBefore, frames.Items.Count);

            window.Close();
        });
    }

    [Fact]
    public void ObjectsCanHaveDifferentVisibilityRanges()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 40);
            var window = new GifEditorWindow(recorder);

            var first = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            var second = AddObject(window, new EllipseItem { Bounds = new SKRect(30, 30, 50, 50) });

            var track = GetTrack(window);
            track.Register(first, 0, 5);
            track.Register(second, 20, 30);

            // Aynı katmanda ama farklı karelerde görünürler.
            Assert.Single(track.ItemsAt(3));
            Assert.Same(first, track.ItemsAt(3)[0]);

            Assert.Single(track.ItemsAt(25));
            Assert.Same(second, track.ItemsAt(25)[0]);

            Assert.Empty(track.ItemsAt(12));
            window.Close();
        });
    }

    [Fact]
    public void NewObject_LivesOnlyOnItsOwnFrame()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            GetTimeline(window).SelectedIndex = 6;
            var item = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });

            // Nesne yalnızca eklendiği karede durur.
            var clip = GetTrack(window).ClipOf(item);
            Assert.Equal(6, clip.StartFrame);
            Assert.Equal(6, clip.EndFrame);
            Assert.False(clip.CoversFrame(5));
            Assert.False(clip.CoversFrame(7));

            window.Close();
        });
    }

    [Fact]
    public void ClearFrame_RemovesObjectsOfThatFrameOnly()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            GetTimeline(window).SelectedIndex = 3;
            AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });

            GetTimeline(window).SelectedIndex = 9;
            var other = AddObject(window, new EllipseItem { Bounds = new SKRect(30, 30, 50, 50) });

            // 3. kareye dön ve temizle.
            GetTimeline(window).SelectedIndex = 3;
            Find<Button>(window, "ClearFrameButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var track = GetTrack(window);
            Assert.Single(track.Scene.Items);
            Assert.Same(other, track.Scene.Items[0]);

            window.Close();
        });
    }

    [Fact]
    public void ClearFrame_DeletesSelectedObjectForItsFullRange()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            var item = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            GetTrack(window).Register(item, 0, 15);
            GetAnnotationCanvas(window).SetSelection(item);

            Find<Button>(window, "ClearFrameButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Empty(GetTrack(window).Scene.Items);
            window.Close();
        });
    }

    [Fact]
    public void ExtendClip_SpansSelectedObjectAcrossWholeGif()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            GetTimeline(window).SelectedIndex = 6;
            var item = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            GetAnnotationCanvas(window).SetSelection(item);

            var extend = Find<Button>(window, "ExtendClipButton");
            Assert.True(extend.IsEnabled);
            extend.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var clip = GetTrack(window).ClipOf(item);
            Assert.Equal(0, clip.StartFrame);
            Assert.Equal(19, clip.EndFrame);
            window.Close();
        });
    }

    [Fact]
    public void TimelineRows_SupportCtrlMultiSelect()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            var first = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            var second = AddObject(window, new EllipseItem { Bounds = new SKRect(30, 30, 50, 50) });

            var canvas = GetAnnotationCanvas(window);
            canvas.SetSelection(first);
            canvas.ToggleSelection(second);

            Assert.Equal(2, canvas.Selection.Count);
            Assert.Contains(first, canvas.Selection);
            Assert.Contains(second, canvas.Selection);
            window.Close();
        });
    }

    [Fact]
    public void ExtendClip_AppliesToEverySelectedBar()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 24);
            var window = new GifEditorWindow(recorder);

            var first = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            var second = AddObject(window, new EllipseItem { Bounds = new SKRect(30, 30, 50, 50) });

            var track = GetTrack(window);
            track.Register(first, 2, 4);
            track.Register(second, 8, 10);

            var canvas = GetAnnotationCanvas(window);
            canvas.SetSelection(new[] { first, second });

            Find<Button>(window, "ExtendClipButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(0, track.ClipOf(first).StartFrame);
            Assert.Equal(23, track.ClipOf(first).EndFrame);
            Assert.Equal(0, track.ClipOf(second).StartFrame);
            Assert.Equal(23, track.ClipOf(second).EndFrame);
            window.Close();
        });
    }

    [Fact]
    public void Objects_GetUniqueNamesAndColors()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            var first = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            var second = AddObject(window, new RectItem { Bounds = new SKRect(30, 30, 50, 50) });

            var track = GetTrack(window);
            var a = track.ClipOf(first);
            var b = track.ClipOf(second);

            Assert.Equal("Dikdörtgen 1", a.Name);
            Assert.Equal("Dikdörtgen 2", b.Name);
            Assert.NotEqual(a.Color, b.Color);

            window.Close();
        });
    }

    [Fact]
    public void Duplicate_GivesCopyItsOwnName()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 20);
            var window = new GifEditorWindow(recorder);

            var item = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            GetAnnotationCanvas(window).SetSelection(item);

            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var track = GetTrack(window);
            var names = track.Scene.Items.Select(i => track.ClipOf(i).Name).ToList();

            Assert.Equal(2, names.Count);
            Assert.Equal(names.Count, names.Distinct().Count());

            window.Close();
        });
    }

    [Fact]
    public void Undo_ReversesFrameEditNotDrawing()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 10);
            var window = new GifEditorWindow(recorder);
            var frames = GetTimeline(window);

            // Önce çizim, sonra kare düzenlemesi.
            AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            int objectsBefore = GetTrack(window).Scene.Items.Count;

            frames.SelectedIndex = 0;
            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(11, frames.Items.Count);

            // Ctrl+Z en son işlemi — kare çoğaltmayı — geri almalı.
            Find<Button>(window, "UndoButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(10, frames.Items.Count);
            Assert.Equal(objectsBefore, GetTrack(window).Scene.Items.Count);

            window.Close();
        });
    }

    [Fact]
    public void Undo_ReversesDrawingWhenItWasLast()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 10);
            var window = new GifEditorWindow(recorder);
            var frames = GetTimeline(window);

            // Önce kare düzenlemesi, sonra çizim.
            frames.SelectedIndex = 0;
            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            int frameCount = frames.Items.Count;

            var canvas = GetAnnotationCanvas(window);
            var track = GetTrack(window);
            track.Scene.Apply(new AddItemAction(new RectItem { Bounds = new SKRect(0, 0, 20, 20) }));
            Assert.Single(track.Scene.Items);

            // Ctrl+Z çizimi geri almalı, kare sayısı korunmalı.
            Find<Button>(window, "UndoButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Empty(track.Scene.Items);
            Assert.Equal(frameCount, frames.Items.Count);

            window.Close();
        });
    }

    [Fact]
    public void UndoButton_DisabledUntilSomethingHappens()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 10);
            var window = new GifEditorWindow(recorder);

            Assert.False(Find<Button>(window, "UndoButton").IsEnabled);

            GetTimeline(window).SelectedIndex = 0;
            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(Find<Button>(window, "UndoButton").IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public void ClipTrim_KeepsObjectOnRemainingFrames()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 30);
            var window = new GifEditorWindow(recorder);

            var item = AddObject(window, new RectItem { Bounds = new SKRect(0, 0, 20, 20) });
            var clip = GetTrack(window).Register(item, 0, 29);

            // Kenardan çekmenin karşılığı: aralık kısalır, nesne durmaya devam eder.
            clip.EndFrame = 10;

            Assert.True(clip.CoversFrame(9));
            Assert.False(clip.CoversFrame(11));
            Assert.Single(GetTrack(window).Scene.Items);

            window.Close();
        });
    }

    [Fact]
    public void Timeline_ExposesPerFrameActions()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 3);
            var window = new GifEditorWindow(recorder);

            // Kare işlemleri artık zaman çizelgesinin bağlam menüsünde.
            var timeline = GetTimeline(window);
            Assert.NotNull(timeline.ContextMenu);

            foreach (string name in new[]
            {
                "MenuDuplicate", "MenuDelete", "MenuMoveLeft", "MenuMoveRight",
                "MenuDeleteBefore", "MenuDeleteAfter", "MenuMarkIn", "MenuMarkOut",
            })
            {
                Assert.NotNull(window.FindName(name));
            }

            window.Close();
        });
    }

    [Fact]
    public void Duplicate_AddsCopyOfSelection()
    {
        WpfRunner.Run(() =>
        {
            using var recorder = MakeRecorder(frameCount: 3);
            var window = new GifEditorWindow(recorder);

            GetTimeline(window).SelectedIndex = 0;
            Find<Button>(window, "DuplicateButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(4, GetTimeline(window).Items.Count);
            window.Close();
        });
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static ListBox GetTimeline(Window window) => Find<ListBox>(window, "Timeline");

    private static InteractiveCanvas GetAnnotationCanvas(Window window)
    {
        // Tuval ilk çizim aracı seçildiğinde kurulur.
        if (Find<ContentControl>(window, "AnnotationHost").Content is not InteractiveCanvas)
            Find<ToggleButton>(window, "ToolSelect").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        var canvas = Find<ContentControl>(window, "AnnotationHost").Content as InteractiveCanvas;
        Assert.NotNull(canvas);
        return canvas!;
    }

    private static AnnotationTrack GetTrack(Window window) => GetAnnotationCanvas(window).Scene switch
    {
        _ => (AnnotationTrack)window.GetType()
            .GetField("_track", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(window)!,
    };

    /// <summary>Sahneye nesne ekler ve kaydını tetikler.</summary>
    private static SceneItem AddObject(Window window, SceneItem item)
    {
        var track = GetTrack(window);
        track.Scene.Items.Add(item);
        track.Scene.RaiseChanged();
        return item;
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement
    {
        var element = window.FindName(name) as T;
        Assert.NotNull(element);
        return element!;
    }

    private static GifRecorder MakeRecorder(int frameCount, int width = 8, int height = 8)
    {
        var recorder = new GifRecorder(new Rectangle(0, 0, width, height));

        for (int i = 0; i < frameCount; i++)
            recorder.TryStoreFrame(MakePixels(width, height, (byte)(i * 30 + 20)), 100);

        return recorder;
    }

    private static byte[] MakePixels(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        return pixels;
    }
}
