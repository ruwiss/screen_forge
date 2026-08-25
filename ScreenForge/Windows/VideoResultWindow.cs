using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenForge.Settings;
using IoPath = System.IO.Path;

namespace ScreenForge.Windows;

internal sealed class VideoResultWindow : Window
{
    private readonly string _path;
    private readonly AppSettings _settings;
    private bool _kept;
    private Point _dragStart;
    private bool _dragging;

    public VideoResultWindow(string path, AppSettings settings)
    {
        _path = path;
        _settings = settings;

        Title = "ScreenForge";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var name = IoPath.GetFileName(path);
        var size = FormatSize(new FileInfo(path).Length);

        var nameBlock = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 18,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        var hintBlock = new TextBlock
        {
            Text = size + "  ·  sürükleyip bırak",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            Margin = new Thickness(0, 4, 0, 0),
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        var drag = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x2D, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 12, 12, 12),
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 0, 0, 12),
            MinHeight = 56,
            Child = new StackPanel { Children = { nameBlock, hintBlock } },
        };
        drag.MouseLeftButtonDown += OnDragDown;
        drag.MouseMove += OnDragMove;
        drag.MouseLeftButtonUp += (_, _) => _dragging = false;

        var save = new Button
        {
            Content = "Kaydet…",
            MinHeight = 36,
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = Cursors.Hand,
            Style = TryFindResource("PrimaryButton") as Style,
        };
        save.Click += (_, _) => SaveAs();

        var close = new Button
        {
            Content = "Kapat",
            MinHeight = 36,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            Style = TryFindResource("SecondaryButton") as Style,
        };
        close.Click += (_, _) => Close();

        var buttons = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);
        buttons.Children.Add(save);

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "Kayıt hazır",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12),
            LineHeight = 22,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        });
        root.Children.Add(drag);
        root.Children.Add(buttons);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = root,
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 20, ShadowDepth = 3, Opacity = 0.45,
        };

        Content = new Grid { Margin = new Thickness(18), Children = { card } };

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Closed += (_, _) =>
        {
            if (!_kept)
                TryDelete(_path);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void OnDragDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < 8 && Math.Abs(pos.Y - _dragStart.Y) < 8)
            return;

        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { _path });
        System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy);
    }

    private void SaveAs()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MP4 video|*.mp4",
            InitialDirectory = _settings.SaveDirectory,
            FileName = IoPath.GetFileName(_path),
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            if (File.Exists(dlg.FileName))
                File.Delete(dlg.FileName);
            File.Copy(_path, dlg.FileName, overwrite: true);
            _settings.SaveDirectory = IoPath.GetDirectoryName(dlg.FileName) ?? _settings.SaveDirectory;
            _settings.Save();
            _kept = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kaydedilemedi: " + ex.Message, "ScreenForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
