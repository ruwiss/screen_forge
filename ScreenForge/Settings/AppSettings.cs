using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenForge.Settings;

/// <summary>
/// Tüm uygulama ayarları + son kullanılan araç stilleri.
/// %AppData%\ScreenForge\settings.json içinde saklanır, anlık kaydedilir.
/// </summary>
public sealed class AppSettings
{
    // ---- Genel ----
    public bool ShowCursor { get; set; } = false;
    public bool AutoCopyLinkAfterUpload { get; set; } = true;
    public bool AutoCloseUploadWindow { get; set; } = false;
    public bool LaunchAtStartup { get; set; } = true;

    // ---- Klavye kısayolları ----
    public HotkeyConfig RegionHotkey { get; set; } = new() { Modifiers = ModifierKeys.Alt | ModifierKeys.Shift, Key = "S" };
    public HotkeyConfig FullScreenHotkey { get; set; } = new() { Modifiers = ModifierKeys.Windows | ModifierKeys.Alt, Key = "F" };
    public HotkeyConfig FullScreenUploadHotkey { get; set; } = new() { Modifiers = ModifierKeys.Windows | ModifierKeys.Alt, Key = "U" };
    public HotkeyConfig CollageHotkey { get; set; } = new() { Modifiers = ModifierKeys.Windows | ModifierKeys.Alt, Key = "C" };

    // ---- Çıktı ----
    public ImageFormat OutputFormat { get; set; } = ImageFormat.Png;
    public int Quality { get; set; } = 92; // JPEG/WebP kalite 1-100
    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    // ---- Son kullanılan araç stilleri (kalıcı, sıfırlanmaz) ----
    public ToolStyleMemory ToolStyles { get; set; } = new();

    // ===================== Kalıcılık =====================

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenForge");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static AppSettings Load(out bool isFirstRun)
    {
        isFirstRun = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.ToolStyles.FontSize = 20;
                    loaded.ToolStyles.StrokeWidth = 4;
                    loaded.ToolStyles.StepSize = 28;
                    return loaded;
                }
            }
        }
        catch
        {
            // Bozuk dosya: varsayılanlara dön.
        }
        isFirstRun = true;
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Yazma hatası sessizce yutulur (disk dolu / izin vs.).
        }
    }
}

public enum ImageFormat
{
    Png,
    Jpeg,
    Webp,
}

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Tek bir global kısayol tanımı (modifier + tuş adı).</summary>
public sealed class HotkeyConfig
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;
    public string Key { get; set; } = "";

    public bool IsValid => !string.IsNullOrWhiteSpace(Key);

    public override string ToString()
    {
        if (!IsValid) return "(atanmadı)";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

/// <summary>
/// Araç çubuğundaki son seçimlerin kalıcı hafızası.
/// Excalidraw/tldraw gibi: kullanıcı bir kez renk/kalınlık seçince hatırlanır.
/// </summary>
public sealed class ToolStyleMemory
{
    // Genel
    public string StrokeColor { get; set; } = "#FFEA6F12";   // accent turuncu
    public string FillColor { get; set; } = "#00000000";     // şeffaf (varsayılan boş)
    public double StrokeWidth { get; set; } = 4;
    public double Opacity { get; set; } = 1.0;

    // Ok başı boyut çarpanı (kalınlıktan bağımsız)
    public double ArrowHeadScale { get; set; } = 1.0;

    // Metin
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 20;
    public bool FontBold { get; set; } = true;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public bool TextShadow { get; set; } = true;
    public int TextShadowLevel { get; set; } = 1;   // 0=Hafif, 1=Normal, 2=Güçlü
    public bool TextStroke { get; set; } = false;
    public string TextStrokeColor { get; set; } = "#FF000000";
    public bool TextRibbon { get; set; } = true;            // paddingli şerit arka plan
    public string TextRibbonColor { get; set; } = "#CC1F2430";

    // Step işareti
    public StepShape StepShape { get; set; } = StepShape.Circle;
    public string StepColor { get; set; } = "#FFE5484D";
    public string StepTextColor { get; set; } = "#FFFFFFFF";
    public double StepSize { get; set; } = 28;

    // Blur / pixelate
    public double BlurStrength { get; set; } = 8;
    public bool BlurPixelate { get; set; } = false;

    // Son seçilen araç
    public string LastTool { get; set; } = "Select";
}

public enum StepShape
{
    Circle,
    Square,
    Bubble,
}
