using ScreenForge.Settings;
using SfModifierKeys = ScreenForge.Settings.ModifierKeys;

namespace ScreenForge.Hotkeys;

/// <summary>Windows'un kendi kullandığı kısayollar. Atamayı engellemez, uyarı metni döner.</summary>
internal static class WindowsReservedHotkeys
{
    private static readonly Dictionary<(SfModifierKeys Mods, string Key), string> Reserved = new()
    {
        [(SfModifierKeys.Alt, "Tab")] = "Görev değiştirme",
        [(SfModifierKeys.Alt, "Escape")] = "Pencere döngüsü",
        [(SfModifierKeys.Alt, "F4")] = "Pencereyi kapat",
        [(SfModifierKeys.Alt, "Space")] = "Pencere menüsü",
        [(SfModifierKeys.Alt, "Snapshot")] = "Pencere görüntüsü",
        [(SfModifierKeys.None, "Snapshot")] = "Ekran görüntüsü",
        [(SfModifierKeys.Control, "Escape")] = "Başlat menüsü",
        [(SfModifierKeys.Control | SfModifierKeys.Shift, "Escape")] = "Görev yöneticisi",
        [(SfModifierKeys.Control | SfModifierKeys.Alt, "Delete")] = "Güvenlik ekranı",
        [(SfModifierKeys.Windows, "D")] = "Masaüstünü göster",
        [(SfModifierKeys.Windows, "E")] = "Dosya Gezgini",
        [(SfModifierKeys.Windows, "L")] = "Kilitle",
        [(SfModifierKeys.Windows, "R")] = "Çalıştır",
        [(SfModifierKeys.Windows, "Tab")] = "Görev görünümü",
        [(SfModifierKeys.Windows, "I")] = "Ayarlar",
        [(SfModifierKeys.Windows, "S")] = "Arama",
        [(SfModifierKeys.Windows, "A")] = "Hızlı ayarlar",
        [(SfModifierKeys.Windows, "X")] = "Hızlı bağlantı",
        [(SfModifierKeys.Windows, "M")] = "Simge durumuna küçült",
        [(SfModifierKeys.Windows, "P")] = "Yansıt",
        [(SfModifierKeys.Windows, "V")] = "Pano",
        [(SfModifierKeys.Windows, "G")] = "Oyun çubuğu",
        [(SfModifierKeys.Windows, "K")] = "Bağlan",
        [(SfModifierKeys.Windows, "H")] = "Sesle yazma",
        [(SfModifierKeys.Windows, "W")] = "Pencere öğeleri",
        [(SfModifierKeys.Windows, "Z")] = "Yapışkan düzenler",
        [(SfModifierKeys.Windows, "U")] = "Erişilebilirlik",
        [(SfModifierKeys.Windows, "Pause")] = "Sistem",
        [(SfModifierKeys.Windows, "Snapshot")] = "Ekran görüntüsü kaydet",
        [(SfModifierKeys.Windows, "Up")] = "Pencere yerleştirme",
        [(SfModifierKeys.Windows, "Down")] = "Pencere yerleştirme",
        [(SfModifierKeys.Windows, "Left")] = "Pencere yerleştirme",
        [(SfModifierKeys.Windows, "Right")] = "Pencere yerleştirme",
        [(SfModifierKeys.Windows | SfModifierKeys.Shift, "S")] = "Ekran alıntısı",
        [(SfModifierKeys.Windows | SfModifierKeys.Shift, "M")] = "Pencereleri geri yükle",
        [(SfModifierKeys.Windows | SfModifierKeys.Control, "D")] = "Yeni masaüstü",
        [(SfModifierKeys.Windows | SfModifierKeys.Control, "Left")] = "Masaüstü değiştir",
        [(SfModifierKeys.Windows | SfModifierKeys.Control, "Right")] = "Masaüstü değiştir",
        [(SfModifierKeys.Windows, "OemPeriod")] = "Emoji",
    };

    static WindowsReservedHotkeys()
    {
        for (int i = 0; i <= 9; i++)
            Reserved[(SfModifierKeys.Windows, "D" + i)] = "Görev çubuğu";
    }

    internal static string? Describe(HotkeyConfig config)
    {
        if (!config.IsValid)
            return null;

        string key = NormalizeKey(config.Key);
        if (!Reserved.TryGetValue((config.Modifiers, key), out string? reason))
            return null;

        return "Windows kullanıyor: " + reason;
    }

    private static string NormalizeKey(string key)
    {
        key = key.Trim();
        if (key.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase)
            || key.Equals("PrtSc", StringComparison.OrdinalIgnoreCase))
            return "Snapshot";
        return key;
    }
}
