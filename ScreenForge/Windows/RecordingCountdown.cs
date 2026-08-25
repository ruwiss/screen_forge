using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenForge.Record;
using ScreenForge.Settings;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace ScreenForge.Windows;

internal static class RecordingCountdown
{
    public static bool Run(Rect dipRegion, AppSettings settings, AudioMixer mixer)
    {
        bool go = false;
        int left = 3;

        var number = new TextBlock
        {
            Text = "3",
            FontSize = 96,
            FontWeight = FontWeights.Bold,
            FontFamily = new WpfFontFamily("Segoe UI"),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var hint = new TextBlock
        {
            Text = "Esc iptal",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };

        var sysBar = Meter("Sistem");
        var micBar = Meter("Mikrofon");
        sysBar.row.Visibility = mixer.HasSystem ? Visibility.Visible : Visibility.Collapsed;
        micBar.row.Visibility = mixer.HasMic ? Visibility.Visible : Visibility.Collapsed;

        var micCombo = new ComboBox
        {
            MinWidth = 220,
            Height = 26,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = mixer.HasMic ? Visibility.Visible : Visibility.Collapsed,
        };
        FillMics(micCombo, settings.Video.MicDeviceId);
        micCombo.SelectionChanged += (_, _) =>
        {
            if (micCombo.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                settings.Video.MicDeviceId = id;
                settings.Save();
                mixer.SetMicrophone(id);
            }
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(number);
        stack.Children.Add(hint);
        stack.Children.Add(sysBar.row);
        stack.Children.Add(micBar.row);
        stack.Children.Add(micCombo);

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x1E, 0x24, 0x32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(28, 20, 28, 22),
            Child = stack,
            Width = Math.Min(360, Math.Max(240, dipRegion.Width)),
        };

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Width = panel.Width,
            SizeToContent = SizeToContent.Height,
            Left = SystemParameters.VirtualScreenLeft + dipRegion.Left + (dipRegion.Width - panel.Width) / 2,
            Top = SystemParameters.VirtualScreenTop + dipRegion.Top + Math.Max(40, (dipRegion.Height - 280) / 2),
            Content = panel,
        };
        win.SourceInitialized += (_, _) => DarkTitleBar.Apply(win);

        var meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        meterTimer.Tick += (_, _) =>
        {
            sysBar.bar.Value = mixer.SystemPeak * 100;
            micBar.bar.Value = mixer.MicPeak * 100;
        };

        var countTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countTimer.Tick += (_, _) =>
        {
            left--;
            if (left <= 0)
            {
                go = true;
                win.Close();
                return;
            }
            number.Text = left.ToString();
        };

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
            else if (e.Key == Key.Space || e.Key == Key.Enter)
            {
                go = true;
                win.Close();
            }
        };
        win.Loaded += (_, _) =>
        {
            meterTimer.Start();
            countTimer.Start();
            win.Activate();
            win.Focus();
        };
        win.Closed += (_, _) =>
        {
            meterTimer.Stop();
            countTimer.Stop();
        };

        win.ShowDialog();
        return go;
    }

    private static void FillMics(ComboBox combo, string selectedId)
    {
        combo.Items.Clear();
        int pick = 0;
        var devices = AudioDevices.ListMicrophones();
        if (devices.Count == 0)
        {
            combo.Items.Add(new ComboBoxItem { Content = "Mikrofon yok", Tag = "" });
            combo.SelectedIndex = 0;
            return;
        }
        for (int i = 0; i < devices.Count; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = devices[i].Name, Tag = devices[i].Id });
            if (devices[i].Id == selectedId) pick = i;
        }
        combo.SelectedIndex = pick;
    }

    private static (DockPanel row, WpfProgressBar bar) Meter(string label)
    {
        var bar = new WpfProgressBar
        {
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x6F, 0x12)),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x45)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = label,
            Width = 72,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB8)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(bar);
        return (row, bar);
    }
}
