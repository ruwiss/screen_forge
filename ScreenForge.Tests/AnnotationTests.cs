using ScreenForge.Editor;
using ScreenForge.Gif.Editing;
using SkiaSharp;

namespace ScreenForge.Tests;

public sealed class AnnotationTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  KARE ARALIĞI
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CoversFrame_RespectsInclusiveRange()
    {
        var clip = new ObjectClip(2, 5);

        Assert.False(clip.CoversFrame(1));
        Assert.True(clip.CoversFrame(2));
        Assert.True(clip.CoversFrame(5));
        Assert.False(clip.CoversFrame(6));
    }

    [Fact]
    public void CoversFrame_ReturnsFalseWhenHidden()
    {
        var clip = new ObjectClip(0, 10) { Visible = false };

        Assert.False(clip.CoversFrame(5));
    }

    [Fact]
    public void SingleFrameClip_CoversOnlyThatFrame()
    {
        var clip = new ObjectClip(7, 7);

        Assert.Equal(1, clip.Length);
        Assert.True(clip.CoversFrame(7));
        Assert.False(clip.CoversFrame(6));
        Assert.False(clip.CoversFrame(8));
    }

    [Fact]
    public void ExtendToAll_SpansWholeDocument()
    {
        var clip = new ObjectClip(4, 4);

        clip.ExtendToAll(frameCount: 20);

        Assert.Equal(0, clip.StartFrame);
        Assert.Equal(19, clip.EndFrame);
    }

    [Fact]
    public void Clamp_KeepsRangeInsideDocument()
    {
        var clip = new ObjectClip(5, 40);

        clip.Clamp(frameCount: 10);

        Assert.Equal(5, clip.StartFrame);
        Assert.Equal(9, clip.EndFrame);
    }

    [Fact]
    public void HideFrom_TrimsTail()
    {
        var clip = new ObjectClip(0, 20);

        Assert.True(clip.HideFrom(10));
        Assert.Equal(9, clip.EndFrame);
        Assert.False(clip.CoversFrame(10));
    }

    [Fact]
    public void HideFrom_ReportsFullRemoval()
    {
        var clip = new ObjectClip(5, 20);

        // Başlangıçtan gizlemek nesneyi tümüyle kaldırmak demektir.
        Assert.False(clip.HideFrom(5));
    }

    [Fact]
    public void ShowFrom_TrimsHead()
    {
        var clip = new ObjectClip(0, 20);

        Assert.True(clip.ShowFrom(8));
        Assert.Equal(8, clip.StartFrame);
        Assert.False(clip.CoversFrame(7));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KONUM
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void OffsetAt_ReturnsEmptyWithoutKeys()
        => Assert.Equal(SKPoint.Empty, new ObjectClip(0, 10).OffsetAt(5));

    [Fact]
    public void OffsetAt_HoldsSingleKeyEverywhere()
    {
        var clip = new ObjectClip(0, 20);
        clip.SetOffsetAt(5, new SKPoint(20, 10));

        Assert.Equal(new SKPoint(20, 10), clip.OffsetAt(0));
        Assert.Equal(new SKPoint(20, 10), clip.OffsetAt(5));
        Assert.Equal(new SKPoint(20, 10), clip.OffsetAt(99));
    }

    [Fact]
    public void OffsetAt_InterpolatesBetweenKeys()
    {
        var clip = new ObjectClip(0, 10);
        clip.SetOffsetAt(0, SKPoint.Empty);
        clip.SetOffsetAt(10, new SKPoint(100, 50));

        var middle = clip.OffsetAt(5);
        Assert.Equal(50, middle.X, 2);
        Assert.Equal(25, middle.Y, 2);

        Assert.Equal(20, clip.OffsetAt(2).X, 2);
    }

    [Fact]
    public void SetOffsetAt_ReplacesExistingFrame()
    {
        var clip = new ObjectClip(0, 10);
        clip.SetOffsetAt(3, new SKPoint(5, 5));
        clip.SetOffsetAt(3, new SKPoint(9, 9));

        Assert.Single(clip.Keys);
        Assert.Equal(new SKPoint(9, 9), clip.OffsetAt(3));
    }

    [Fact]
    public void SetOffsetAt_KeepsKeysSorted()
    {
        var clip = new ObjectClip(0, 10);
        clip.SetOffsetAt(9, new SKPoint(90, 0));
        clip.SetOffsetAt(1, new SKPoint(10, 0));
        clip.SetOffsetAt(5, new SKPoint(50, 0));

        Assert.Equal(new[] { 1, 5, 9 }, clip.Keys.Select(k => k.Frame));
    }

    [Fact]
    public void IsMoving_RequiresTwoKeys()
    {
        var clip = new ObjectClip(0, 10);
        Assert.False(clip.IsMoving);

        clip.SetOffsetAt(0, SKPoint.Empty);
        Assert.False(clip.IsMoving);

        clip.SetOffsetAt(4, new SKPoint(10, 0));
        Assert.True(clip.IsMoving);
    }

    [Fact]
    public void PreviousAndNextKeyFrame_FindNeighbours()
    {
        var clip = new ObjectClip(0, 20);
        clip.SetOffsetAt(2, SKPoint.Empty);
        clip.SetOffsetAt(8, new SKPoint(10, 0));
        clip.SetOffsetAt(14, new SKPoint(20, 0));

        Assert.Equal(8, clip.PreviousKeyFrame(10));
        Assert.Equal(14, clip.NextKeyFrame(10));
        Assert.Equal(-1, clip.PreviousKeyFrame(0));
        Assert.Equal(-1, clip.NextKeyFrame(99));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KARE KAYDIRMA
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ShiftForFrameChange_MovesRangeAndKeys()
    {
        var clip = new ObjectClip(4, 6);
        clip.SetOffsetAt(5, new SKPoint(10, 0));

        clip.ShiftForFrameChange(at: 2, delta: 3);

        Assert.Equal(7, clip.StartFrame);
        Assert.Equal(9, clip.EndFrame);
        Assert.Equal(8, clip.Keys[0].Frame);
    }

    [Fact]
    public void ShiftForFrameChange_IgnoresChangesAfterRange()
    {
        var clip = new ObjectClip(1, 3);
        clip.SetOffsetAt(2, new SKPoint(4, 4));

        clip.ShiftForFrameChange(at: 8, delta: 5);

        Assert.Equal(1, clip.StartFrame);
        Assert.Equal(3, clip.EndFrame);
        Assert.Equal(2, clip.Keys[0].Frame);
    }

    [Fact]
    public void ShiftForFrameChange_NeverProducesNegativeFrames()
    {
        var clip = new ObjectClip(1, 4);
        clip.SetOffsetAt(2, SKPoint.Empty);

        clip.ShiftForFrameChange(at: 0, delta: -10);

        Assert.True(clip.StartFrame >= 0);
        Assert.True(clip.EndFrame >= clip.StartFrame);
        Assert.True(clip.Keys[0].Frame >= 0);
    }

    [Fact]
    public void Clone_ProducesIndependentClip()
    {
        var clip = new ObjectClip(2, 8);
        clip.SetOffsetAt(3, new SKPoint(5, 5));

        var copy = clip.Clone();
        copy.StartFrame = 40;
        copy.SetOffsetAt(9, new SKPoint(1, 1));

        Assert.Equal(2, clip.StartFrame);
        Assert.Single(clip.Keys);
        Assert.Equal(2, copy.Keys.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KATMAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClipOf_ReturnsSameInstancePerItem()
    {
        var track = MakeTrack();
        var item = MakeRect(0, 0, 10, 10);
        track.Scene.Items.Add(item);

        Assert.Same(track.ClipOf(item), track.ClipOf(item));
    }

    [Fact]
    public void EachObject_KeepsItsOwnRange()
    {
        var track = MakeTrack();

        var early = MakeRect(0, 0, 10, 10);
        var late = MakeRect(20, 20, 30, 30);
        track.Scene.Items.Add(early);
        track.Scene.Items.Add(late);

        track.Register(early, 0, 4);
        track.Register(late, 10, 20);

        Assert.Single(track.ItemsAt(2));
        Assert.Same(early, track.ItemsAt(2)[0]);

        Assert.Single(track.ItemsAt(15));
        Assert.Same(late, track.ItemsAt(15)[0]);

        Assert.Empty(track.ItemsAt(7));
    }

    [Fact]
    public void ItemsAt_PreservesDrawOrder()
    {
        var track = MakeTrack();

        var bottom = MakeRect(0, 0, 10, 10);
        var top = MakeRect(2, 2, 12, 12);
        track.Scene.Items.Add(bottom);
        track.Scene.Items.Add(top);

        track.Register(bottom, 0, 10);
        track.Register(top, 0, 10);

        var visible = track.ItemsAt(5);

        Assert.Equal(2, visible.Count);
        Assert.Same(bottom, visible[0]);
        Assert.Same(top, visible[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  AD VE RENK
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Register_GivesSequentialNamesPerType()
    {
        var track = MakeTrack();

        var first = MakeRect(0, 0, 10, 10);
        var second = MakeRect(5, 5, 15, 15);
        track.Scene.Items.Add(first);
        track.Scene.Items.Add(second);

        var a = track.Register(first, 0, 0, "Dikdörtgen");
        var b = track.Register(second, 1, 1, "Dikdörtgen");

        Assert.Equal("Dikdörtgen 1", a.Name);
        Assert.Equal("Dikdörtgen 2", b.Name);
    }

    [Fact]
    public void Register_CountsEachTypeSeparately()
    {
        var track = MakeTrack();

        var rect = MakeRect(0, 0, 10, 10);
        var ellipse = new EllipseItem { Bounds = new SKRect(0, 0, 10, 10) };
        track.Scene.Items.Add(rect);
        track.Scene.Items.Add(ellipse);

        var a = track.Register(rect, 0, 0, "Dikdörtgen");
        var b = track.Register(ellipse, 0, 0, "Elips");

        Assert.Equal("Dikdörtgen 1", a.Name);
        Assert.Equal("Elips 1", b.Name);
    }

    [Fact]
    public void Register_GivesDistinctColors()
    {
        var track = MakeTrack();

        var first = MakeRect(0, 0, 10, 10);
        var second = MakeRect(5, 5, 15, 15);
        track.Scene.Items.Add(first);
        track.Scene.Items.Add(second);

        var a = track.Register(first, 0, 0, "Dikdörtgen");
        var b = track.Register(second, 1, 1, "Dikdörtgen");

        Assert.NotEqual(a.Color, b.Color);
    }

    [Fact]
    public void RegisterCopy_ReusesRangeButRenames()
    {
        var track = MakeTrack();

        var original = MakeRect(0, 0, 10, 10);
        track.Scene.Items.Add(original);
        var source = track.Register(original, 4, 9, "Dikdörtgen");

        var copy = MakeRect(2, 2, 12, 12);
        track.Scene.Items.Add(copy);
        var clip = track.RegisterCopy(copy, source, "Dikdörtgen");

        Assert.Equal("Dikdörtgen 2", clip.Name);
        Assert.Equal(4, clip.StartFrame);
        Assert.Equal(9, clip.EndFrame);
    }

    [Fact]
    public void Palette_ProducesDistinctToneForEachIndex()
    {
        var colors = Enumerable.Range(0, 6).Select(ObjectPalette.ColorFor).ToList();

        Assert.Equal(colors.Count, colors.Distinct().Count());
    }

    [Fact]
    public void ClearFrame_RemovesSingleFrameObjects()
    {
        var track = MakeTrack();

        var item = MakeRect(0, 0, 10, 10);
        track.Scene.Items.Add(item);
        track.Register(item, 6, 6);

        int changed = track.ClearFrame(6);

        Assert.Equal(1, changed);
        Assert.Empty(track.Scene.Items);
    }

    [Fact]
    public void ClearFrame_TrimsMultiFrameObjectsInstead()
    {
        var track = MakeTrack();

        var item = MakeRect(0, 0, 10, 10);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 10);

        int changed = track.ClearFrame(0);

        // Nesne diğer karelerde durmaya devam eder.
        Assert.Equal(1, changed);
        Assert.Single(track.Scene.Items);
        Assert.False(track.ClipOf(item).CoversFrame(0));
        Assert.True(track.ClipOf(item).CoversFrame(5));
    }

    [Fact]
    public void ClearFrame_IgnoresFramesWithoutObjects()
        => Assert.Equal(0, MakeTrack().ClearFrame(3));

    [Fact]
    public void ShiftForFrameChange_AppliesToEveryObject()
    {
        var track = MakeTrack();

        var first = MakeRect(0, 0, 5, 5);
        var second = MakeRect(6, 6, 12, 12);
        track.Scene.Items.Add(first);
        track.Scene.Items.Add(second);

        track.Register(first, 2, 4);
        track.Register(second, 5, 9);

        track.ShiftForFrameChange(at: 0, delta: 2);

        Assert.Equal(4, track.ClipOf(first).StartFrame);
        Assert.Equal(7, track.ClipOf(second).StartFrame);
    }

    [Fact]
    public void HasVisibleItems_ReflectsPerObjectVisibility()
    {
        var track = MakeTrack();
        Assert.False(track.HasVisibleItems());

        var item = MakeRect(1, 1, 9, 9);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 5);
        Assert.True(track.HasVisibleItems());

        track.ClipOf(item).Visible = false;
        Assert.False(track.HasVisibleItems());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KOMPOZİT
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_ReturnsSameArrayWithoutItems()
    {
        var pixels = MakePixels(16, 16, 0x30);

        var result = AnnotationCompositor.Apply(pixels, 16, 16, MakeTrack(), 0);

        Assert.Same(pixels, result);
    }

    [Fact]
    public void Apply_DrawsOnlyOnCoveredFrames()
    {
        var pixels = MakePixels(32, 32, 0x30);
        var track = MakeTrack();

        var item = MakeRect(4, 4, 28, 28);
        track.Scene.Items.Add(item);
        track.Register(item, 5, 8);

        Assert.Same(pixels, AnnotationCompositor.Apply(pixels, 32, 32, track, frameIndex: 2));
        Assert.NotSame(pixels, AnnotationCompositor.Apply(pixels, 32, 32, track, frameIndex: 6));
    }

    [Fact]
    public void Apply_DrawsObjectAtItsPerFramePosition()
    {
        var pixels = MakePixels(64, 64, 0x20);
        var track = MakeTrack();

        var item = MakeRect(0, 0, 12, 12);
        track.Scene.Items.Add(item);
        var clip = track.Register(item, 0, 10);

        clip.SetOffsetAt(0, SKPoint.Empty);
        clip.SetOffsetAt(10, new SKPoint(40, 40));

        var atStart = AnnotationCompositor.Apply(pixels, 64, 64, track, frameIndex: 0);
        var atEnd = AnnotationCompositor.Apply(pixels, 64, 64, track, frameIndex: 10);

        Assert.NotEqual(atStart, atEnd);
    }

    [Fact]
    public void Apply_DoesNotMutateSourceFrame()
    {
        var pixels = MakePixels(32, 32, 0x30);
        var original = (byte[])pixels.Clone();

        var track = MakeTrack();
        var item = MakeRect(8, 8, 24, 24);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 4);

        var result = AnnotationCompositor.Apply(pixels, 32, 32, track, frameIndex: 1);

        Assert.Equal(original, pixels);
        Assert.NotEqual(original, result);
    }

    [Fact]
    public void Apply_KeepsEveryPixelOpaque()
    {
        var pixels = MakePixels(24, 24, 0x40);
        var track = MakeTrack();

        var item = MakeRect(4, 4, 20, 20);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 2);

        var result = AnnotationCompositor.Apply(pixels, 24, 24, track, frameIndex: 0);

        for (int i = 3; i < result.Length; i += 4)
            Assert.Equal(255, result[i]);
    }

    [Fact]
    public void Apply_SkipsRequestedItems()
    {
        var pixels = MakePixels(32, 32, 0x30);
        var track = MakeTrack();

        var item = MakeRect(4, 4, 28, 28);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 5);

        // Canlı düzenlenen nesne kompozitten çıkarılır; çift çizim olmasın.
        var result = AnnotationCompositor.Apply(pixels, 32, 32, track, 1, skip: new[] { item });

        Assert.Same(pixels, result);
    }

    [Fact]
    public void Apply_ScalesForResizedOutput()
    {
        var pixels = MakePixels(32, 32, 0x30);
        var track = MakeTrack();

        var item = MakeRect(16, 16, 48, 48);
        track.Scene.Items.Add(item);
        track.Register(item, 0, 3);

        var result = AnnotationCompositor.Apply(pixels, 32, 32, track,
            frameIndex: 0, sourceWidth: 64, sourceHeight: 64);

        Assert.Equal(pixels.Length, result.Length);
        Assert.NotEqual(pixels, result);
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static AnnotationTrack MakeTrack() => new(new SKSize(64, 64));

    private static RectItem MakeRect(float left, float top, float right, float bottom) => new()
    {
        Bounds = new SKRect(left, top, right, bottom),
        StrokeColor = new SKColor(0xFF, 0x00, 0x00),
        FillColor = new SKColor(0xFF, 0x00, 0x00),
        StrokeWidth = 3,
    };

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
