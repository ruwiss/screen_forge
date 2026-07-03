using Microsoft.Win32;
using System.IO;

namespace ScreenForge.Settings;

/// <summary>Windows ile birlikte başlatma (HKCU Run anahtarı).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenForge";
    private const string AppName = "ScreenForge";
    private const string MainExeName = "ScreenForge.exe";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enabled)
            {
                string exe = ResolveStartupExe();
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry erişimi başarısızsa sessizce yut.
        }
    }

    private static string ResolveStartupExe()
    {
        var processPath = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(processPath))
            return "";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyInstallDir = Path.Combine(localAppData, "Programs", AppName);
        var velopackExe = Path.Combine(localAppData, AppName, "current", MainExeName);

        if (File.Exists(velopackExe) && IsUnderDirectory(processPath, legacyInstallDir))
            return velopackExe;

        return processPath;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
