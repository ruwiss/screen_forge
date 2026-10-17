using ScreenForge.Settings;

namespace ScreenForge.Tests;

/// <summary>
/// Ayar dosyasının okuma/yazma davranışı: atomik yazma, bozuk dosyada yedek,
/// yarım (tmp) dosya bırakmama.
/// </summary>
public sealed class AppSettingsFileTests
{
    [Fact]
    public void Save_WhenRenameFails_RemovesOnlyItsTemporaryFile()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            Directory.CreateDirectory(path); // Hedef bir dizin: atomik taşıma başarısız olmalı.
            new AppSettings().Save(path);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.True(Directory.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_ConcurrentWritersAndReader_NeverExposePartialSettings()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var settings = new AppSettings
            {
                RegionHotkey = new HotkeyConfig { Key = "S", Modifiers = ModifierKeys.Control | ModifierKeys.Shift }
            };
            settings.Save(path);
            var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Parallel.For(0, 100, i =>
            {
                new AppSettings { RegionHotkey = new HotkeyConfig { Key = "S", Modifiers = ModifierKeys.Control | ModifierKeys.Shift } }.Save(path);
                var loaded = AppSettings.Load(path, out bool first);
                if (first || loaded.RegionHotkey.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
                    failures.Enqueue($"{i}: kısayol varsayılana döndü");
            });
            Assert.Empty(failures);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.False(File.Exists(path + ".corrupt"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_JsonNull_PreservesOriginalAndCorruptBackup()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "null");
            AppSettings.Load(path, out bool first);
            Assert.True(first);
            Assert.Equal("null", File.ReadAllText(path));
            Assert.Equal("null", File.ReadAllText(path + ".corrupt"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sf-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsHotkeysAndFlags()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var settings = new AppSettings
            {
                ShowCursor = true,
                RegionHotkey = new HotkeyConfig { Key = "S", Modifiers = ModifierKeys.Control | ModifierKeys.Shift },
            };
            settings.QuickTranslateHotkey.Key = "T";
            settings.QuickTranslateHotkey.Modifiers = ModifierKeys.Control | ModifierKeys.Shift;

            settings.Save(path);
            var loaded = AppSettings.Load(path, out bool isFirstRun);

            Assert.False(isFirstRun);
            Assert.True(loaded.ShowCursor);
            Assert.Equal("S", loaded.RegionHotkey.Key);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, loaded.RegionHotkey.Modifiers);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, loaded.QuickTranslateHotkey.Modifiers);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_KeepsBackupAndReturnsDefaults()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ bu geçerli bir JSON değil ");

            var loaded = AppSettings.Load(path, out bool isFirstRun);

            Assert.True(isFirstRun);
            Assert.True(loaded.RegionHotkey.IsValid);
            Assert.Equal("S", loaded.RegionHotkey.Key);
            Assert.True(File.Exists(path + ".corrupt"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_LeavesNoTempFilesBehind()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new AppSettings().Save(path);
            new AppSettings { ShowCursor = true }.Save(path);

            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_StartsFresh()
    {
        var dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "settings.json");

            var loaded = AppSettings.Load(path, out bool isFirstRun);

            Assert.True(isFirstRun);
            Assert.True(loaded.RegionHotkey.IsValid);
            Assert.False(File.Exists(path + ".corrupt"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
