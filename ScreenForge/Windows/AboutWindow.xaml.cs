using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace ScreenForge.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        TxtVersion.Text = "Sürüm " + GetAppVersion();
        BtnOk.Click += (_, _) => Close();
        LnkGitHub.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/ruwiss") { UseShellExecute = true }); } catch { } };
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // "1.0.9+hash" → "1.0.9"
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
