using System.Diagnostics;
using System.Windows;

namespace ScreenForge.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        BtnOk.Click += (_, _) => Close();
        LnkGitHub.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/ruwiss") { UseShellExecute = true }); } catch { } };
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }
}
