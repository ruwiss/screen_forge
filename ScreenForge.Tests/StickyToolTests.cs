using ScreenForge.Editor;
using ScreenForge.Settings;
using SkiaSharp;

namespace ScreenForge.Tests;

/// <summary>
/// Sticky araç + soft-select: çizim araçları create sonrası Select'e düşmez;
/// seçim option bar için kalır, tutamaçlar yalnızca Select'te.
/// </summary>
public sealed class StickyToolTests
{
    [Fact]
    public void SwitchingToDrawTool_ClearsSelection()
    {
        WpfRunner.Run(() =>
        {
            var canvas = MakeCanvas();
            var item = new RectItem { Bounds = new SKRect(0, 0, 40, 30) };
            canvas.Scene.Items.Add(item);
            canvas.SetSelection(item);

            canvas.Tool = EditorTool.Arrow;

            Assert.Equal(EditorTool.Arrow, canvas.Tool);
            Assert.Empty(canvas.Selection);
        });
    }

    [Fact]
    public void SwitchingToSelect_KeepsSoftSelection()
    {
        WpfRunner.Run(() =>
        {
            var canvas = MakeCanvas();
            var item = new RectItem { Bounds = new SKRect(0, 0, 40, 30) };
            canvas.Scene.Items.Add(item);
            canvas.Tool = EditorTool.Rectangle;
            canvas.SetSelection(item); // soft-select while sticky

            canvas.Tool = EditorTool.Select;

            Assert.Equal(EditorTool.Select, canvas.Tool);
            Assert.Same(item, canvas.SelectedItem);
        });
    }

    [Fact]
    public void ReassigningSameDrawTool_KeepsSoftSelection()
    {
        WpfRunner.Run(() =>
        {
            var canvas = MakeCanvas();
            var item = new ArrowItem { Start = new SKPoint(0, 0), End = new SKPoint(50, 50) };
            canvas.Scene.Items.Add(item);
            canvas.Tool = EditorTool.Arrow;
            canvas.SetSelection(item);

            canvas.Tool = EditorTool.Arrow; // toolbar'a tekrar tık

            Assert.Equal(EditorTool.Arrow, canvas.Tool);
            Assert.Same(item, canvas.SelectedItem);
        });
    }

    [Fact]
    public void SwitchingBetweenDrawTools_ClearsSelection()
    {
        WpfRunner.Run(() =>
        {
            var canvas = MakeCanvas();
            var item = new RectItem { Bounds = new SKRect(0, 0, 20, 20) };
            canvas.Scene.Items.Add(item);
            canvas.Tool = EditorTool.Rectangle;
            canvas.SetSelection(item);

            canvas.Tool = EditorTool.Ellipse;

            Assert.Equal(EditorTool.Ellipse, canvas.Tool);
            Assert.Empty(canvas.Selection);
        });
    }

    private static InteractiveCanvas MakeCanvas()
    {
        var scene = new Scene { CanvasSize = new SKSize(200, 200) };
        return new InteractiveCanvas(scene, new ToolStyleMemory());
    }
}
