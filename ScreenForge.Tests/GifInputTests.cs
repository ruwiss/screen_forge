using System.Drawing;
using System.Windows.Input;
using ScreenForge.Gif;
using ScreenForge.Gif.Input;

namespace ScreenForge.Tests;

public sealed class GifInputTests
{
    // ─── Tuş etiketleri ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(Key.LeftCtrl, "Ctrl")]
    [InlineData(Key.RightCtrl, "Ctrl")]
    [InlineData(Key.LeftShift, "Shift")]
    [InlineData(Key.A, "A")]
    [InlineData(Key.D5, "5")]
    [InlineData(Key.NumPad3, "3")]
    [InlineData(Key.F7, "F7")]
    [InlineData(Key.Return, "Enter")]
    [InlineData(Key.Left, "←")]
    public void Describe_MapsKeysToLabels(Key key, string expected)
        => Assert.Equal(expected, KeyLabels.Describe(key));

    [Fact]
    public void Describe_ReturnsNullForUnlabelledKeys()
        => Assert.Null(KeyLabels.Describe(Key.None));

    [Fact]
    public void Order_PutsModifiersFirstAndDeduplicates()
    {
        var keys = new[] { Key.S, Key.LeftShift, Key.LeftCtrl, Key.RightCtrl };

        var ordered = KeyLabels.Order(keys).ToList();

        // Ctrl iki kez basılı ama tek etiket; değiştiriciler normal tuştan önce.
        Assert.Equal(new[] { "Ctrl", "Shift", "S" }, ordered);
    }

    // ─── Kaplama çizimi ───────────────────────────────────────────────────────

    [Fact]
    public void Apply_ReturnsSameArrayWhenNoInput()
    {
        var frame = MakeFrame(16, 16, 0x40);
        var options = new InputOverlayOptions();

        var result = InputOverlayRenderer.Apply(frame, 16, 16, null, options);

        Assert.Same(frame, result);
    }

    [Fact]
    public void Apply_ReturnsSameArrayWhenOverlayDisabled()
    {
        var frame = MakeFrame(16, 16, 0x40);
        var input = new FrameInput { CursorVisible = true, ClickStarted = true, CursorX = 8, CursorY = 8 };
        var options = new InputOverlayOptions { HighlightClicks = false, ShowKeys = false };

        var result = InputOverlayRenderer.Apply(frame, 16, 16, input, options);

        Assert.Same(frame, result);
    }

    [Fact]
    public void Apply_DrawsClickHighlightWithoutMutatingSource()
    {
        var frame = MakeFrame(64, 64, 0x30);
        var original = (byte[])frame.Clone();
        var input = new FrameInput
        {
            CursorX = 32,
            CursorY = 32,
            CursorVisible = true,
            ClickStarted = true,
            Buttons = MouseButtons.Left,
        };

        var result = InputOverlayRenderer.Apply(frame, 64, 64, input, new InputOverlayOptions());

        Assert.NotSame(frame, result);
        Assert.Equal(original, frame); // kaynak değişmedi
        Assert.NotEqual(original, result);

        // Tıklama merkezinde piksel değişmiş olmalı.
        int center = (32 * 64 + 32) * 4;
        Assert.True(result[center] != original[center]
                 || result[center + 1] != original[center + 1]
                 || result[center + 2] != original[center + 2]);
    }

    [Fact]
    public void Apply_KeepsAllPixelsOpaque()
    {
        var frame = MakeFrame(48, 48, 0x50);
        var input = new FrameInput { CursorX = 24, CursorY = 24, CursorVisible = true, ClickStarted = true };
        input.Keys.Add("Ctrl");
        input.Keys.Add("S");

        var result = InputOverlayRenderer.Apply(frame, 48, 48, input, new InputOverlayOptions());

        for (int i = 3; i < result.Length; i += 4)
            Assert.Equal(255, result[i]);
    }

    [Fact]
    public void Apply_DrawsKeyBadgeInBottomLeft()
    {
        var frame = MakeFrame(160, 90, 0x20);
        var original = (byte[])frame.Clone();
        var input = new FrameInput();
        input.Keys.Add("Ctrl");
        input.Keys.Add("C");

        var result = InputOverlayRenderer.Apply(frame, 160, 90, input, new InputOverlayOptions());

        Assert.NotEqual(original, result);

        // Sol alt bölgede değişiklik olmalı, sağ üst bölge dokunulmamış kalmalı.
        Assert.True(RegionChanged(original, result, 160, x: 10, y: 70, w: 40, h: 15));
        Assert.False(RegionChanged(original, result, 160, x: 120, y: 5, w: 30, h: 15));
    }

    [Fact]
    public void Apply_ScalesOverlayForResizedOutput()
    {
        var frame = MakeFrame(32, 32, 0x30);
        var input = new FrameInput { CursorX = 16, CursorY = 16, CursorVisible = true, ClickStarted = true };

        // Yarı ölçekte çizim de yarıya iner ve kare sınırları içinde kalır.
        var result = InputOverlayRenderer.Apply(frame, 32, 32, input, new InputOverlayOptions(), scale: 0.5);

        Assert.NotSame(frame, result);
        Assert.Equal(frame.Length, result.Length);
    }

    // ─── Kayıt entegrasyonu ───────────────────────────────────────────────────

    [Fact]
    public void TryStoreFrame_KeepsInputsParallelToFrames()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 1, 1), maxFrameBytes: 64);

        var input = new FrameInput { CursorX = 3, CursorY = 4, CursorVisible = true };
        recorder.TryStoreFrame(new byte[4], 100, input);
        recorder.TryStoreFrame(new byte[4], 100);

        Assert.Equal(2, recorder.FrameInputs.Count);
        Assert.Equal(3, recorder.FrameInputs[0].CursorX);
        Assert.False(recorder.FrameInputs[1].CursorVisible);
    }

    [Fact]
    public void DetachFrames_ReturnsInputsAlignedWithFrames()
    {
        using var recorder = new GifRecorder(new Rectangle(0, 0, 1, 1), maxFrameBytes: 64);
        recorder.TryStoreFrame(new byte[4], 100, new FrameInput { CursorVisible = true });
        recorder.TryStoreFrame(new byte[4], 100, new FrameInput());

        var recording = recorder.DetachFrames();

        Assert.Equal(recording.Frames.Count, recording.Inputs.Count);
        Assert.True(recording.Inputs[0].CursorVisible);
        Assert.Empty(recorder.FrameInputs);
    }

    [Fact]
    public async Task SaveAsync_WithOverlayProducesDifferentBytesThanWithout()
    {
        string withOverlay = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");
        string plain = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            const int Size = 64;
            var frames = new List<byte[]> { MakeFrame(Size, Size, 0x30), MakeFrame(Size, Size, 0x30) };

            var inputs = new List<FrameInput>
            {
                new() { CursorX = 32, CursorY = 32, CursorVisible = true, ClickStarted = true, Buttons = MouseButtons.Left },
                new(),
            };

            using var recorder = new GifRecorder(new Rectangle(0, 0, Size, Size));

            await recorder.SaveAsync(withOverlay, new GifExportOptions
            {
                Frames = frames,
                FrameInputs = inputs,
                Width = Size,
                Height = Size,
                InputOverlay = new InputOverlayOptions(),
            });

            await recorder.SaveAsync(plain, new GifExportOptions
            {
                Frames = frames,
                FrameInputs = inputs,
                Width = Size,
                Height = Size,
                InputOverlay = null,
            });

            var a = await File.ReadAllBytesAsync(withOverlay);
            var b = await File.ReadAllBytesAsync(plain);

            Assert.NotEqual(a.Length, b.Length);
            Assert.Equal(0x3b, a[^1]);
        }
        finally
        {
            TryDelete(withOverlay);
            TryDelete(plain);
        }
    }

    [Fact]
    public async Task SaveAsync_OverlayAppliesBeforeDeltaSoHighlightSurvives()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ScreenForge_{Guid.NewGuid():N}.gif");

        try
        {
            const int Size = 64;
            // İki kare piksel olarak aynı; fark yalnızca ikincisindeki tıklama.
            var frames = new List<byte[]> { MakeFrame(Size, Size, 0x30), MakeFrame(Size, Size, 0x30) };
            var inputs = new List<FrameInput>
            {
                new(),
                new() { CursorX = 32, CursorY = 32, CursorVisible = true, ClickStarted = true },
            };

            using var recorder = new GifRecorder(new Rectangle(0, 0, Size, Size));
            await recorder.SaveAsync(path, new GifExportOptions
            {
                Frames = frames,
                FrameDelays = new List<int> { 100, 100 },
                FrameInputs = inputs,
                Width = Size,
                Height = Size,
                OptimizeUnchangedPixels = true,
                InputOverlay = new InputOverlayOptions(),
            });

            using var image = System.Drawing.Image.FromFile(path);
            var dimension = new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]);

            // Kaplama delta hesabından önce uygulandığı için kareler artık farklı
            // ve ikisi de yazılır. Uygulanmasaydı tek kareye inerdi.
            Assert.Equal(2, image.GetFrameCount(dimension));
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ─── Yardımcılar ──────────────────────────────────────────────────────────

    private static byte[] MakeFrame(int width, int height, byte value)
    {
        var frame = new byte[width * height * 4];
        for (int i = 0; i < frame.Length; i += 4)
        {
            frame[i] = value;
            frame[i + 1] = value;
            frame[i + 2] = value;
            frame[i + 3] = 255;
        }
        return frame;
    }

    private static bool RegionChanged(byte[] a, byte[] b, int width, int x, int y, int w, int h)
    {
        for (int row = y; row < y + h; row++)
        {
            for (int col = x; col < x + w; col++)
            {
                int i = (row * width + col) * 4;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
                    return true;
            }
        }
        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* geçici dosya */ }
    }
}
