using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ScreenForge.Upload;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenForge.Windows;

public sealed class FileDropUploadWindow
{
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    public void Show()
    {
        Window? win = null;

        var dropIcon = new TextBlock
        {
            Text = "↑",
            FontSize = 34,
            FontWeight = FontWeights.Thin,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x68, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        var dropLabel = new TextBlock
        {
            Text = "Resim veya GIF sürükleyin",
            FontSize = 11,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x84, 0xA4)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var dropHint = new TextBlock
        {
            Text = "PNG · JPG · GIF · WebP",
            FontSize = 9,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x58, 0x72)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            IsHitTestVisible = false,
        };

        var dropStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        dropStack.Children.Add(dropIcon);
        dropStack.Children.Add(dropLabel);
        dropStack.Children.Add(dropHint);

        var dashedRect = new WpfRectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x3E, 0x4E, 0x68)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = Brushes.Transparent,
            RadiusX = 8, RadiusY = 8,
            IsHitTestVisible = false,
        };

        var dropZoneInner = new Grid { Width = 160, Height = 130 };
        dropZoneInner.Children.Add(dashedRect);
        dropZoneInner.Children.Add(dropStack);

        var dropZoneWrap = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x2A)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Child = dropZoneInner,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        Window? winRef = null;
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 26, Height = 26,
            FontSize = 12, FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0xBE, 0x3A, 0x3A)),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeBtn.Click += (_, _) => winRef?.Close();

        var titleTxt = new TextBlock
        {
            Text = "Yükleme Yap",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new WpfFontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleTxt, 0);
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(titleTxt);
        titleBar.Children.Add(closeBtn);

        var content = new StackPanel { Margin = new Thickness(12, 10, 10, 12) };
        content.Children.Add(titleBar);
        content.Children.Add(dropZoneWrap);

        var panelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x44, 0x5A)),
            BorderThickness = new Thickness(1, 1, 0, 0),
            CornerRadius = new CornerRadius(10, 0, 0, 0),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };

        win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = panelBorder,
            AllowDrop = true,
        };
        winRef = win;

        win.Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            win.Left = wa.Right - win.ActualWidth;
            win.Top = wa.Bottom - win.ActualHeight;
        };

        void SetDropHighlight(bool on)
        {
            dropZoneWrap.Background = new SolidColorBrush(on
                ? Color.FromRgb(0x22, 0x34, 0x50)
                : Color.FromRgb(0x18, 0x1E, 0x2A));
            dashedRect.Stroke = new SolidColorBrush(on
                ? Color.FromRgb(0x4C, 0x8C, 0xD0)
                : Color.FromRgb(0x3E, 0x4E, 0x68));
            dropIcon.Foreground = new SolidColorBrush(on
                ? Color.FromRgb(0x5B, 0x9B, 0xD5)
                : Color.FromRgb(0x55, 0x68, 0x88));
        }

        win.DragEnter += (_, e) =>
        {
            if (IsValidDrop(e)) { e.Effects = WpfDragDropEffects.Copy; SetDropHighlight(true); }
            else e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
        };
        win.DragOver += (_, e) =>
        {
            e.Effects = IsValidDrop(e) ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
            e.Handled = true;
        };
        win.DragLeave += (_, _) => SetDropHighlight(false);

        win.Drop += (_, e) =>
        {
            SetDropHighlight(false);
            e.Handled = true;

            var files = e.Data.GetData(WpfDataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return;

            var path = files[0];
            if (!AllowedExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) return;

            try
            {
                if (new FileInfo(path).Length > MaxFileSizeBytes)
                {
                    MessageBox.Show("Dosya boyutu 4 MB sınırını aşıyor.", "ScreenForge",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch { return; }

            win!.Close();
            StartUpload(path);
        };


        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) win.Close(); };
        win.Show();
    }

    private const long MaxFileSizeBytes = 4 * 1024 * 1024; // 4 MB

    private static bool IsValidDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return false;
        var files = e.Data.GetData(WpfDataFormats.FileDrop) as string[];
        if (files == null || files.Length != 1) return false;
        var path = files[0];
        if (!AllowedExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) return false;
        try { return new FileInfo(path).Length <= MaxFileSizeBytes; } catch { return false; }
    }

    private static async void StartUpload(string path)
    {
        UploadToastWindow? toast = null;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif"            => "image/gif",
                ".webp"           => "image/webp",
                _                 => "image/png",
            };

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                toast = new UploadToastWindow();
                toast.Show();
            });

            IUploadProvider provider = new PrntscrUploadProvider();
            var result = await provider.UploadAsync(bytes, mime, p =>
                Application.Current.Dispatcher.Invoke(() => toast?.ReportProgress(p)));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                toast?.ShowResult(result.Url);
                Clipboard.SetText(result.Url);
                toast?.SetCopied();
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                toast?.Close();
                MessageBox.Show("Yükleme başarısız: " + ex.Message, "ScreenForge",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
    }
}
