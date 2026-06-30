using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenForge.Gif;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

/// <summary>
/// Transparent full-screen overlay shown during GIF recording.
/// Shows a dashed border around the capture region + a control bar above it.
/// No screen dimming — the recording area is fully visible.
/// </summary>
public sealed class GifRecordingOverlayWindow
{
    public event Action<GifRecorder>? Stopped;

    private readonly GifRecorder _recorder;
    private readonly Rect _dipRegion;

    public GifRecordingOverlayWindow(GifRecorder recorder, Rect dipRegion)
    {
        _recorder = recorder;
        _dipRegion = dipRegion;
    }

    public void Show()
    {
        Window? win = null;
        TextBlock? elapsedText = null;
        TextBlock? frameText = null;
        TextBlock? recDot = null;

        // ─── Dashed border ───────────────────────────────────────────────────
        var borderRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            StrokeDashCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Width = _dipRegion.Width,
            Height = _dipRegion.Height,
        };

        // ─── Control bar ─────────────────────────────────────────────────────
        recDot = new TextBlock
        {
            Text = "●",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };

        elapsedText = new TextBlock
        {
            Text = "00:00",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        frameText = new TextBlock
        {
            Text = "0 kare",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            FontSize = 10,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var stopBtn = new Button
        {
            Content = "Durdur",
            Width = 72, Height = 26,
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Background = new SolidColorBrush(Color.FromRgb(0xBE, 0x3A, 0x3A)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var barStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        barStack.Children.Add(recDot);
        barStack.Children.Add(elapsedText);
        barStack.Children.Add(frameText);
        barStack.Children.Add(stopBtn);

        var controlBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x24, 0x32)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Child = barStack,
        };

        // ─── Full-screen canvas ───────────────────────────────────────────────
        var rootCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
        };
        rootCanvas.Children.Add(borderRect);
        rootCanvas.Children.Add(controlBar);

        Canvas.SetLeft(borderRect, _dipRegion.Left);
        Canvas.SetTop(borderRect, _dipRegion.Top);

        // control bar positioned above region; will adjust after measure
        Canvas.SetLeft(controlBar, _dipRegion.Left);
        Canvas.SetTop(controlBar, Math.Max(0, _dipRegion.Top - 44));

        // stopBtn needs a reference to win so set after win creation
        win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            IsHitTestVisible = false,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Content = rootCanvas,
        };

        // control bar must accept clicks — override canvas hit-test for it
        controlBar.IsHitTestVisible = true;
        win.IsHitTestVisible = true;

        stopBtn.Click += (_, _) =>
        {
            _recorder.Stop();
            win!.Close();
            Stopped?.Invoke(_recorder);
        };

        // ─── Timers ───────────────────────────────────────────────────────────
        var uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        uiTimer.Tick += (_, _) =>
        {
            var e = _recorder.Elapsed;
            elapsedText!.Text = $"{(int)e.TotalMinutes:D2}:{e.Seconds:D2}";
            frameText!.Text = $"{_recorder.FrameCount} kare";
        };

        var blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool blinkOn = true;
        blinkTimer.Tick += (_, _) =>
        {
            blinkOn = !blinkOn;
            recDot!.Opacity = blinkOn ? 1.0 : 0.2;
        };

        win.Loaded += (_, _) =>
        {
            uiTimer.Start();
            blinkTimer.Start();
        };

        win.Closed += (_, _) => { uiTimer.Stop(); blinkTimer.Stop(); };
        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) stopBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

        win.Show();
    }
}
