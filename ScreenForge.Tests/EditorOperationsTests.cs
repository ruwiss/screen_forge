using System.Windows;
using ScreenForge.Gif.Editing;
using ScreenForge.Gif.Input;

namespace ScreenForge.Tests;

public sealed class EditorOperationsTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  BELGE / GERİ ALMA
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_PushesUndoAndClearsRedo()
    {
        var doc = new EditorDocument(MakeFrames(3), 4, 4);

        doc.Apply("sil", MakeFrames(2));
        Assert.True(doc.CanUndo);
        Assert.Equal("sil", doc.UndoLabel);

        doc.Undo();
        Assert.True(doc.CanRedo);

        // Geri alma sonrası yeni düzenleme yineleme geçmişini geçersiz kılar.
        doc.Apply("çoğalt", MakeFrames(5));
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void Undo_And_Redo_RestoreExactState()
    {
        var original = MakeFrames(3);
        var doc = new EditorDocument(original, 4, 4);
        var edited = MakeFrames(7);

        doc.Apply("düzenle", edited);
        Assert.Equal(7, doc.FrameCount);

        doc.Undo();
        Assert.Same(original, doc.Frames);

        doc.Redo();
        Assert.Same(edited, doc.Frames);
    }

    [Fact]
    public void Apply_IgnoresIdenticalState()
    {
        var frames = MakeFrames(3);
        var doc = new EditorDocument(frames, 4, 4);

        doc.Apply("işlemsiz", frames);

        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void History_DropsOldestBeyondLimit()
    {
        var doc = new EditorDocument(MakeFrames(1), 4, 4, historyLimit: 2);

        doc.Apply("bir", MakeFrames(2));
        doc.Apply("iki", MakeFrames(3));
        doc.Apply("üç", MakeFrames(4));

        // Sınır 2: yalnızca son iki adım geri alınabilir.
        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        Assert.False(doc.Undo());
    }

    [Fact]
    public void Apply_TracksSizeChange()
    {
        var doc = new EditorDocument(MakeFrames(2), 8, 6);

        doc.Apply("döndür", MakeFrames(2), 6, 8);

        Assert.Equal(6, doc.Width);
        Assert.Equal(8, doc.Height);

        doc.Undo();
        Assert.Equal(8, doc.Width);
        Assert.Equal(6, doc.Height);
    }

    [Fact]
    public void Statistics_ReflectFrameDelays()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(4, 4, 0x10, delay: 100),
            MakeFrame(4, 4, 0x20, delay: 200),
            MakeFrame(4, 4, 0x30, delay: 300),
        };
        var doc = new EditorDocument(frames, 4, 4);

        Assert.Equal(600, doc.Current.TotalDuration.TotalMilliseconds);
        Assert.Equal(200, doc.Current.AverageDelay);
        Assert.Equal(300, doc.TimeUpTo(1).TotalMilliseconds);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KARE İŞLEMLERİ
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Remove_AddsDelayToPreviousFrame()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(2, 2, 0x10, delay: 100),
            MakeFrame(2, 2, 0x20, delay: 200),
            MakeFrame(2, 2, 0x30, delay: 300),
        };

        var result = FrameOperations.Remove(frames, new[] { 1 }, DelayMergeMode.AddToPrevious);

        Assert.Equal(2, result.Count);
        Assert.Equal(300, result[0].Delay);   // 100 + 200
        Assert.Equal(300, result[1].Delay);
    }

    [Fact]
    public void Remove_DiscardModeShortensAnimation()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(2, 2, 0x10, delay: 100),
            MakeFrame(2, 2, 0x20, delay: 200),
        };

        var result = FrameOperations.Remove(frames, new[] { 1 }, DelayMergeMode.Discard);

        Assert.Single(result);
        Assert.Equal(100, result[0].Delay);
    }

    [Fact]
    public void Remove_KeepsAtLeastOneFrame()
    {
        var frames = MakeFrames(2);

        var result = FrameOperations.Remove(frames, new[] { 0, 1 });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Remove_FirstFrameCarriesDelayForward()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(2, 2, 0x10, delay: 100),
            MakeFrame(2, 2, 0x20, delay: 200),
        };

        // Önceki kare yok; gecikme sonrakine aktarılmalı.
        var result = FrameOperations.Remove(frames, new[] { 0 }, DelayMergeMode.AddToPrevious);

        Assert.Single(result);
        Assert.Equal(300, result[0].Delay);
    }

    [Fact]
    public void RemoveBefore_And_RemoveAfter_KeepAnchorFrame()
    {
        var frames = MakeFrames(5);

        Assert.Equal(3, FrameOperations.RemoveBefore(frames, 2).Count);
        Assert.Equal(3, FrameOperations.RemoveAfter(frames, 2).Count);
    }

    [Fact]
    public void Trim_KeepsInclusiveRange()
    {
        var frames = MakeFrames(6);

        var result = FrameOperations.Trim(frames, 1, 3);

        Assert.Equal(3, result.Count);
        Assert.Same(frames[1], result[0]);
        Assert.Same(frames[3], result[2]);
    }

    [Fact]
    public void Duplicate_InsertsCopyAfterEachSelection()
    {
        var frames = MakeFrames(3);

        var result = FrameOperations.Duplicate(frames, new[] { 0, 2 });

        Assert.Equal(5, result.Count);
        Assert.Same(frames[0], result[0]);
        Assert.Same(frames[0], result[1]);
        Assert.Same(frames[2], result[4]);
    }

    [Fact]
    public void Reverse_InvertsOrder()
    {
        var frames = MakeFrames(3);

        var result = FrameOperations.Reverse(frames);

        Assert.Same(frames[2], result[0]);
        Assert.Same(frames[0], result[2]);
    }

    [Fact]
    public void MoveLeft_And_MoveRight_SwapNeighbours()
    {
        var frames = MakeFrames(3);

        var left = FrameOperations.MoveLeft(frames, new[] { 1 });
        Assert.Same(frames[1], left[0]);
        Assert.Same(frames[0], left[1]);

        var right = FrameOperations.MoveRight(frames, new[] { 1 });
        Assert.Same(frames[2], right[1]);
        Assert.Same(frames[1], right[2]);
    }

    [Fact]
    public void MoveLeft_IgnoresFirstFrame()
    {
        var frames = MakeFrames(3);

        var result = FrameOperations.MoveLeft(frames, new[] { 0 });

        Assert.Same(frames[0], result[0]);
    }

    // ─── Gecikme ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetDelay_OnlyTouchesSelection()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(2, 2, 0x10, delay: 100),
            MakeFrame(2, 2, 0x20, delay: 100),
        };

        var result = FrameOperations.SetDelay(frames, new[] { 1 }, 500);

        Assert.Equal(100, result[0].Delay);
        Assert.Equal(500, result[1].Delay);
    }

    [Fact]
    public void ScaleDelay_HalvesDuration()
    {
        var frames = new List<EditorFrame> { MakeFrame(2, 2, 0x10, delay: 200) };

        var result = FrameOperations.ScaleDelay(frames, new[] { 0 }, 50);

        Assert.Equal(100, result[0].Delay);
    }

    [Fact]
    public void AdjustDelay_ClampsToMinimum()
    {
        var frames = new List<EditorFrame> { MakeFrame(2, 2, 0x10, delay: 20) };

        var result = FrameOperations.AdjustDelay(frames, new[] { 0 }, -100);

        // GIF için anlamlı alt sınır 10 ms.
        Assert.Equal(10, result[0].Delay);
    }

    [Fact]
    public void SetFps_AppliesUniformDelay()
    {
        var result = FrameOperations.SetFps(MakeFrames(3), 20);

        Assert.All(result, f => Assert.Equal(50, f.Delay));
    }

    // ─── Azaltma / yinelenenler ───────────────────────────────────────────────

    [Fact]
    public void Reduce_DropsFramesAndPreservesTotalDuration()
    {
        var frames = Enumerable.Range(0, 10)
            .Select(i => MakeFrame(2, 2, (byte)(i * 10), delay: 100))
            .ToList();

        var result = FrameOperations.Reduce(frames, keep: 2, remove: 1, DelayMergeMode.Distribute);

        Assert.True(result.Count < frames.Count);
        Assert.Equal(frames.Sum(f => f.Delay), result.Sum(f => f.Delay));
    }

    [Fact]
    public void Reduce_KeepsLastFramePixels()
    {
        var frames = Enumerable.Range(0, 9)
            .Select(i => MakeFrame(2, 2, (byte)(i * 10), delay: 100))
            .ToList();

        var result = FrameOperations.Reduce(frames, keep: 1, remove: 1);

        // Animasyonun bitişi korunur; gecikme dağıtım nedeniyle değişebilir.
        Assert.Same(frames[^1].Pixels, result[^1].Pixels);
    }

    [Fact]
    public void RemoveDuplicates_CollapsesIdenticalRun()
    {
        var pixels = MakePixels(4, 4, 0x40);
        var frames = new List<EditorFrame>
        {
            MakeFrameWith(pixels, 100),
            MakeFrameWith(pixels, 100),
            MakeFrameWith(pixels, 100),
            MakeFrame(4, 4, 0x90, delay: 100),
        };

        var result = FrameOperations.RemoveDuplicates(frames);

        Assert.Equal(2, result.Count);
        Assert.Equal(300, result[0].Delay);   // üç aynı kare birleşti
    }

    [Fact]
    public void RemoveDuplicates_RespectsSimilarityThreshold()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(4, 4, 0x40, delay: 100),
            MakeNearlyIdentical(4, 4, 0x40, changedPixels: 2, delay: 100),
        };

        // 16 pikselin 2'si farklı → %87.5 benzerlik.
        Assert.Equal(2, FrameOperations.RemoveDuplicates(frames, similarity: 95).Count);
        Assert.Single(FrameOperations.RemoveDuplicates(frames, similarity: 80));
    }

    [Fact]
    public void RemoveDuplicates_KeepsFramesCarryingInput()
    {
        var pixels = MakePixels(4, 4, 0x40);
        var withClick = new EditorFrame
        {
            Pixels = pixels,
            Delay = 100,
            Input = new FrameInput { CursorVisible = true, Buttons = MouseButtons.Left, ClickStarted = true },
        };

        var frames = new List<EditorFrame> { MakeFrameWith(pixels, 100), withClick };

        // Görsel olarak aynı ama tıklama taşıyor → korunmalı.
        Assert.Equal(2, FrameOperations.RemoveDuplicates(frames, keepFramesWithInput: true).Count);
        Assert.Single(FrameOperations.RemoveDuplicates(frames, keepFramesWithInput: false));
    }

    [Fact]
    public void SmoothLoop_TrimsTailMatchingFirstFrame()
    {
        var first = MakePixels(4, 4, 0x22);
        var frames = new List<EditorFrame>
        {
            MakeFrameWith(first, 100),
            MakeFrame(4, 4, 0x55, delay: 100),
            MakeFrame(4, 4, 0x88, delay: 100),
            MakeFrameWith(first, 100),   // ilk kareyle aynı → döngüde gereksiz
        };

        var result = FrameOperations.SmoothLoop(frames, similarity: 99);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SmoothLoop_LeavesSequenceWithoutLoopPoint()
    {
        var frames = new List<EditorFrame>
        {
            MakeFrame(4, 4, 0x11, delay: 100),
            MakeFrame(4, 4, 0x55, delay: 100),
            MakeFrame(4, 4, 0x99, delay: 100),
        };

        Assert.Equal(3, FrameOperations.SmoothLoop(frames, similarity: 99).Count);
    }

    [Fact]
    public void Similarity_ReportsExactMatch()
    {
        var a = MakePixels(4, 4, 0x30);
        var b = MakePixels(4, 4, 0x30);

        Assert.Equal(100, FrameOperations.Similarity(a, b));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GÖRÜNTÜ İŞLEMLERİ
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Crop_ReducesFrameToRect()
    {
        var frames = new List<EditorFrame> { MakeFrame(8, 8, 0x30, delay: 100) };

        var result = ImageOperations.Crop(frames, 8, 8, new Int32Rect(2, 2, 4, 4));

        Assert.Equal(4 * 4 * 4, result[0].Pixels.Length);
    }

    [Fact]
    public void Crop_ClampsRectToBounds()
    {
        var rect = ImageOperations.ClampRect(new Int32Rect(6, 6, 10, 10), 8, 8);

        Assert.Equal(new Int32Rect(6, 6, 2, 2), rect);
    }

    [Fact]
    public void Rotate_Right90_MovesTopLeftToTopRight()
    {
        // 2×1: [A][B] → sağa 90° → 1×2 dikey: A üstte, B altta
        var pixels = new byte[2 * 1 * 4];
        SetPixel(pixels, 0, 10, 20, 30);
        SetPixel(pixels, 1, 40, 50, 60);

        var rotated = ImageOperations.RotatePixels(pixels, 2, 1, RotateDirection.Right90);

        Assert.Equal(10, rotated[0]);      // A ilk sırada
        Assert.Equal(40, rotated[4]);      // B ikinci sırada
    }

    [Fact]
    public void Rotate_Half180_ReversesPixels()
    {
        var pixels = new byte[2 * 1 * 4];
        SetPixel(pixels, 0, 10, 20, 30);
        SetPixel(pixels, 1, 40, 50, 60);

        var rotated = ImageOperations.RotatePixels(pixels, 2, 1, RotateDirection.Half180);

        Assert.Equal(40, rotated[0]);
        Assert.Equal(10, rotated[4]);
    }

    [Fact]
    public void ScreenRectToSource_DividesByZoom()
    {
        // %50 yakınlaştırmada ekrandaki 100×80 seçim kaynakta 200×160 olur.
        var rect = ImageOperations.ScreenRectToSource(20, 10, 100, 80,
            zoom: 0.5, sourceWidth: 400, sourceHeight: 300);

        Assert.Equal(new Int32Rect(40, 20, 200, 160), rect);
    }

    [Fact]
    public void ScreenRectToSource_IsIdentityAtFullZoom()
    {
        var rect = ImageOperations.ScreenRectToSource(5, 7, 30, 40,
            zoom: 1.0, sourceWidth: 100, sourceHeight: 100);

        Assert.Equal(new Int32Rect(5, 7, 30, 40), rect);
    }

    [Fact]
    public void ScreenRectToSource_ClampsToImageBounds()
    {
        // Seçim sağ kenarı aşıyor; kaynak sınırına kırpılmalı.
        var rect = ImageOperations.ScreenRectToSource(180, 0, 100, 50,
            zoom: 1.0, sourceWidth: 200, sourceHeight: 100);

        Assert.Equal(new Int32Rect(180, 0, 20, 50), rect);
    }

    [Fact]
    public void ScreenRectToSource_RejectsTinySelection()
    {
        Assert.Null(ImageOperations.ScreenRectToSource(0, 0, 1, 1,
            zoom: 1.0, sourceWidth: 100, sourceHeight: 100));

        Assert.Null(ImageOperations.ScreenRectToSource(0, 0, 0, 0,
            zoom: 1.0, sourceWidth: 100, sourceHeight: 100));
    }

    [Fact]
    public void ScreenRectToSource_MatchesCropOutput()
    {
        // Kırpma sonucu boyutu, hesaplanan dikdörtgenle birebir örtüşmeli.
        var rect = ImageOperations.ScreenRectToSource(30, 20, 60, 40,
            zoom: 2.0, sourceWidth: 100, sourceHeight: 80);

        Assert.NotNull(rect);

        var frames = new List<EditorFrame> { MakeFrame(100, 80, 0x30, delay: 100) };
        var cropped = ImageOperations.Crop(frames, 100, 80, rect!.Value);

        Assert.Equal(rect.Value.Width * rect.Value.Height * 4, cropped[0].Pixels.Length);
    }

    [Fact]
    public void SizeAfterRotate_SwapsForQuarterTurns()
    {
        Assert.Equal((6, 8), ImageOperations.SizeAfterRotate(8, 6, RotateDirection.Right90));
        Assert.Equal((6, 8), ImageOperations.SizeAfterRotate(8, 6, RotateDirection.Left90));
        Assert.Equal((8, 6), ImageOperations.SizeAfterRotate(8, 6, RotateDirection.Half180));
    }

        [Fact]
    public void Resize_ProducesRequestedDimensions()
    {
        var frames = new List<EditorFrame> { MakeFrame(16, 16, 0x50, delay: 100) };

        var result = ImageOperations.Resize(frames, 16, 16, 8, 8);

        Assert.Equal(8 * 8 * 4, result[0].Pixels.Length);
    }

    [Fact]
    public void Resize_KeepsPixelsOpaque()
    {
        var frames = new List<EditorFrame> { MakeFrame(8, 8, 0x50, delay: 100) };

        var result = ImageOperations.Resize(frames, 8, 8, 4, 4);

        for (int i = 3; i < result[0].Pixels.Length; i += 4)
            Assert.Equal(255, result[0].Pixels[i]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ═══════════════════════════════════════════════════════════════════════════

    private static List<EditorFrame> MakeFrames(int count)
        => Enumerable.Range(0, count).Select(i => MakeFrame(4, 4, (byte)(i * 20 + 10), 100)).ToList();

    private static EditorFrame MakeFrame(int width, int height, byte value, int delay)
        => MakeFrameWith(MakePixels(width, height, value), delay);

    private static EditorFrame MakeFrameWith(byte[] pixels, int delay)
        => new() { Pixels = pixels, Delay = delay, Input = new FrameInput() };

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

    private static EditorFrame MakeNearlyIdentical(int width, int height, byte value, int changedPixels, int delay)
    {
        var pixels = MakePixels(width, height, value);
        for (int p = 0; p < changedPixels; p++)
            SetPixel(pixels, p, 0xFF, 0xFF, 0xFF);

        return MakeFrameWith(pixels, delay);
    }

    private static void SetPixel(byte[] pixels, int index, byte b, byte g, byte r)
    {
        int offset = index * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = 255;
    }
}
