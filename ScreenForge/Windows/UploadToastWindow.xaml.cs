using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ScreenForge.Windows;

public partial class UploadToastWindow : Window
{
    private string? _url;

    public UploadToastWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        BtnClose.Click += (_, _) => Close();
        BtnCopyLink.Click += (_, _) => { if (_url != null) { Clipboard.SetText(_url); } Close(); };
        BtnOpen.Click += (_, _) => { if (_url != null) try { Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); } catch { } Close(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
        PlayEntryAnimation();
    }

    private void PlayEntryAnimation()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void ReportProgress(double value)
    {
        Dispatcher.Invoke(() =>
        {
            var v = Math.Clamp(value, 0, 1);
            var anim = new DoubleAnimation(ProgressScale.ScaleX, v, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            PercentText.Text = $"%{(int)(v * 100)}";
        });
    }

    public void ShowResult(string url)
    {
        Dispatcher.Invoke(() =>
        {
            _url = url;
            TitleText.Text = "Yüklendi";
            PercentText.Visibility = Visibility.Collapsed;
            ProgressTrack.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            LinkText.Text = url;

            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            CloudIcon.Visibility = Visibility.Collapsed;
            CheckIcon.Visibility = Visibility.Visible;

            var area = SystemParameters.WorkArea;
            UpdateLayout();
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        });
    }

    public void SetCopied()
    {
        Dispatcher.Invoke(() =>
        {
            TitleText.Text = "Bağlantı kopyalandı";
            BtnCopyLink.Content = "Kopyalandı";
            BtnCopyLink.IsEnabled = false;
        });
    }

    public void AutoCloseSoon()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
