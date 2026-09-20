using System.Globalization;
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
    public HotkeyConfig FullScreenHotkey { get; set; } = new();
    public HotkeyConfig FullScreenUploadHotkey { get; set; } = new();
    public HotkeyConfig CollageHotkey { get; set; } = new();
    public HotkeyConfig QuickTranslateHotkey { get; set; } = new();

    // ---- Çıktı ----
    public ImageFormat OutputFormat { get; set; } = ImageFormat.Png;
    public int Quality { get; set; } = 92; // JPEG/WebP kalite 1-100
    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    // ---- Son kullanılan araç stilleri (kalıcı, sıfırlanmaz) ----
    public ToolStyleMemory ToolStyles { get; set; } = new();

    // ---- GIF kayıt/dışa aktarma tercihleri ----
    public GifSettings Gif { get; set; } = new();

    // ---- Video kayıt tercihleri ----
    public VideoSettings Video { get; set; } = new();

    // ---- Canlı sunum (ZoomIt-benzeri) ----
    public PresenterSettings Presenter { get; set; } = new();

    // ---- Çeviri ----
    /// <summary>Kaynak dil kodu; "auto" = otomatik algıla (görüntü çevirisi).</summary>
    public string TranslateSourceLanguage { get; set; } = "auto";
    /// <summary>Eski JSON alanı; yeni kod ana dili kullanır.</summary>
    public string TranslateTargetLanguage { get; set; } = "tr";
    /// <summary>Ana dil (görüntü çevirisi hedefi; hızlı çeviride varsayılan hedef).</summary>
    public string TranslateNativeLanguage { get; set; } = "";
    /// <summary>Çevrilecek dil (kaynak zaten ana dilse hızlı çevirinin hedefi).</summary>
    public string TranslatePairLanguage { get; set; } = "";

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
                var loaded = Deserialize(json);
                if (loaded != null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // Bozuk dosya: varsayılanlara dön.
        }
        isFirstRun = true;
        var created = new AppSettings();
        created.Normalize();
        return created;
    }

    internal static AppSettings? Deserialize(string json)
        => JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

    /// <summary>Eski kayıtlardan gelen ve artık kalıcı olmayan alanları sıfırlar.</summary>
    public void Normalize() => Normalize(CultureInfo.CurrentUICulture);

    internal void Normalize(CultureInfo uiCulture)
    {
        // Opacity kaydedilmez; her oturumda varsayılan %100.
        ToolStyles.Opacity = 1.0;

        Gif.Fps = Math.Clamp(Gif.Fps, 1, 60);
        Video.Fps = Math.Clamp(Video.Fps, 1, 60);

        Presenter ??= new PresenterSettings();
        Presenter.Normalize();

        if (string.IsNullOrWhiteSpace(TranslateNativeLanguage))
            TranslateNativeLanguage = TranslateLanguageDefaults.MapUiCulture(uiCulture);
        if (string.IsNullOrWhiteSpace(TranslatePairLanguage))
            TranslatePairLanguage = TranslateLanguageDefaults.DefaultPair(TranslateNativeLanguage);
    }

    public void Save()
    {
        try
        {
            Normalize();
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

/// <summary>GIF kaydı ve dışa aktarımı için hatırlanan tercihler.</summary>
public sealed class GifSettings
{
    /// <summary>
    /// Yakalama hızı (1-60). Varsayılan 20: GIF gecikmesi 1/100 sn biriminde
    /// tutulduğu için 20 fps tam 5 santisaniyeye oturur ve sapma olmaz.
    /// (15 fps 6.67cs ister, 7cs'e yuvarlanır → gerçekte ~14.3 fps oynar.)
    /// </summary>
    public int Fps { get; set; } = 20;

    /// <summary>Palet boyutu: 256, 128 veya 64.</summary>
    public int ColorCount { get; set; } = 256;

    /// <summary>Neural = kaliteli, Octree = hızlı.</summary>
    public string Quantizer { get; set; } = "Neural";

    /// <summary>Neural örnekleme faktörü (1 = en iyi kalite, 20 = en hızlı).</summary>
    public int SamplingFactor { get; set; } = 5;

    public bool Dithering { get; set; }

    /// <summary>Tüm kareler için tek palet — dosya boyutunu belirgin düşürür.</summary>
    public bool UseGlobalPalette { get; set; }

    /// <summary>Değişmeyen bölgeleri kırp ve saydam yaz.</summary>
    public bool OptimizeUnchangedPixels { get; set; } = true;

    /// <summary>"Değişmedi" sayılması için kanal başına izin verilen fark (0-32).</summary>
    public int ChangeTolerance { get; set; }

    /// <summary>
    /// Kare belleği üst sınırı (MB). Kareler sıkıştırılmış saklandığı için
    /// bu bütçe ham piksel karşılığının 20-30 katına denk gelir.
    /// </summary>
    public int MaxFrameMemoryMb { get; set; } = 1024;

    // ---- Girdi yakalama ----

    /// <summary>Fare imlecini kayda dahil et.</summary>
    public bool CaptureCursor { get; set; } = true;

    /// <summary>Fare tıklamalarını izle ve renkli daire ile vurgula.</summary>
    public bool HighlightClicks { get; set; } = true;

    /// <summary>
    /// Tıklama olmasa da imlecin etrafında sürekli vurgu göster.
    /// İmleci takip etmeyi kolaylaştırır; varsayılan kapalı.
    /// </summary>
    public bool HighlightCursor { get; set; }

    /// <summary>Klavyeyi izle ve basılan tuşları rozet olarak göster.</summary>
    public bool ShowKeys { get; set; } = true;

    /// <summary>Vurgu dairesinin yarıçapı (kaynak piksel).</summary>
    public double HighlightRadius { get; set; } = 12;
}

/// <summary>Canlı sunum katmanı tercihleri ve kısayolları.</summary>
public sealed class PresenterSettings
{
    public int DefaultsRevision { get; set; }

    public bool Enabled { get; set; } = true;

    public bool PenEnabled { get; set; }
    public HotkeyConfig PenHotkey { get; set; } = new();
    public bool LaserEnabled { get; set; }
    public HotkeyConfig LaserHotkey { get; set; } = new();
    public bool RectangleEnabled { get; set; }
    public HotkeyConfig RectangleHotkey { get; set; } = new();
    public bool EllipseEnabled { get; set; }
    public HotkeyConfig EllipseHotkey { get; set; } = new();
    public bool ArrowEnabled { get; set; }
    public HotkeyConfig ArrowHotkey { get; set; } = new();
    public bool LineEnabled { get; set; }
    public HotkeyConfig LineHotkey { get; set; } = new();
    public bool SpotlightEnabled { get; set; }
    public HotkeyConfig SpotlightHotkey { get; set; } = new();
    public bool ArrowBendEnabled { get; set; }
    public HotkeyConfig ArrowBendHotkey { get; set; } = new();
    public bool CancelEnabled { get; set; } = true;
    public HotkeyConfig CancelHotkey { get; set; } = new() { Key = "Escape" };

    public string PenColor { get; set; } = "#FFEA6F12";
    public double PenWidth { get; set; } = 5;
    public string LaserColor { get; set; } = "#FFFF3D6E";
    public double LaserWidth { get; set; } = 2;
    public int LaserFadeMs { get; set; } = 700;
    public double SpotlightRadius { get; set; } = 200;
    public double SpotlightSoftness { get; set; } = 20;
    public double SpotlightDim { get; set; } = 0.40;
    public int ZoomAnimationMs { get; set; } = 280;

    public List<PresenterZoomPreset> ZoomPresets { get; set; } =
    [
        new() { Factor = 2 },
        new() { Factor = 4 },
    ];

    public List<PresenterColorBind> ColorBinds { get; set; } = [];

    public void Normalize()
    {
        PenWidth = Math.Clamp(PenWidth, 1, 24);
        LaserWidth = Math.Clamp(LaserWidth, 1, 24);
        LaserFadeMs = Math.Clamp(LaserFadeMs, 150, 3000);
        SpotlightRadius = Math.Clamp(SpotlightRadius, 40, 600);
        SpotlightSoftness = Math.Clamp(SpotlightSoftness, 8, 80);
        SpotlightDim = Math.Clamp(SpotlightDim, 0.15, 0.85);
        ZoomAnimationMs = Math.Clamp(ZoomAnimationMs, 80, 800);
        if (DefaultsRevision < 4)
            ApplyZoomItDefaults();
        if (string.IsNullOrWhiteSpace(PenColor)) PenColor = "#FFEA6F12";
        if (string.IsNullOrWhiteSpace(LaserColor)) LaserColor = "#FFFF3D6E";
        if (CancelHotkey is not { IsValid: true })
            CancelHotkey = new HotkeyConfig { Key = "Escape" };
        if (ArrowBendHotkey is not { IsValid: true })
            ArrowBendHotkey = new HotkeyConfig { Key = "Space" };
        if (IsOldSpotlightFactory())
        {
            SpotlightRadius = 200;
            SpotlightSoftness = 20;
            SpotlightDim = 0.40;
        }
        if (Math.Abs(LaserWidth - 6) < 0.01 && string.Equals(LaserColor, "#FFE5484D", StringComparison.OrdinalIgnoreCase)
            || string.Equals(LaserColor, "#CCE5484D", StringComparison.OrdinalIgnoreCase)
            || string.Equals(LaserColor, "#FFE5484D", StringComparison.OrdinalIgnoreCase))
        {
            if (Math.Abs(LaserWidth - 6) < 0.01) LaserWidth = 3;
            LaserColor = "#FFFF3D6E";
        }
        ZoomPresets ??= [];
        if (ZoomPresets.Count == 0)
        {
            ZoomPresets.Add(new PresenterZoomPreset { Factor = 2 });
            ZoomPresets.Add(new PresenterZoomPreset { Factor = 4 });
        }
        foreach (var z in ZoomPresets)
        {
            z.Hotkey ??= new HotkeyConfig();
            z.Factor = Math.Clamp(z.Factor, 1.25, 8);
        }
        ColorBinds ??= [];
        foreach (var c in ColorBinds)
        {
            c.Hotkey ??= new HotkeyConfig();
            if (string.IsNullOrWhiteSpace(c.Color)) c.Color = "#FFEA6F12";
        }
    }

    private bool IsOldSpotlightFactory() =>
        SpotlightSoftness > 50
        || Near(SpotlightRadius, 155) && Near(SpotlightSoftness, 28) && Near(SpotlightDim, 0.52)
        || Near(SpotlightRadius, 155) && Near(SpotlightSoftness, 175) && Near(SpotlightDim, 0.52)
        || Near(SpotlightRadius, 180) && Near(SpotlightSoftness, 90) && Near(SpotlightDim, 0.55)
        || Near(SpotlightRadius, 120) && Near(SpotlightSoftness, 36) && Near(SpotlightDim, 0.64);

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.02;

    public void ApplyZoomItDefaults()
    {
        PenEnabled = true;
        PenHotkey = Ctrl("D2");
        LaserEnabled = true;
        LaserHotkey = Ctrl("D3");
        RectangleEnabled = false;
        RectangleHotkey = new();
        EllipseEnabled = false;
        EllipseHotkey = new();
        ArrowEnabled = false;
        ArrowHotkey = new();
        LineEnabled = false;
        LineHotkey = new();
        SpotlightEnabled = true;
        SpotlightHotkey = Ctrl("D4");
        ArrowBendEnabled = false;
        ArrowBendHotkey = new();
        CancelEnabled = true;
        CancelHotkey = new HotkeyConfig { Key = "Escape" };
        LaserWidth = 2;
        ZoomPresets =
        [
            new() { Factor = 1.5, Enabled = true, Hotkey = Ctrl("D1") },
            new() { Factor = 2, Enabled = false, Hotkey = new() },
        ];
        ColorBinds = [];
        DefaultsRevision = 4;
    }

    private static HotkeyConfig Ctrl(string key, bool shift = false) => new()
    {
        Modifiers = shift ? ModifierKeys.Control | ModifierKeys.Shift : ModifierKeys.Control,
        Key = key,
    };
}

public sealed class PresenterZoomPreset
{
    public bool Enabled { get; set; }
    public double Factor { get; set; } = 2;
    public HotkeyConfig Hotkey { get; set; } = new();
}

public sealed class PresenterColorBind
{
    public string Color { get; set; } = "#FFEA6F12";
    public HotkeyConfig Hotkey { get; set; } = new();
}

public enum VideoQuality
{
    Low,
    Medium,
    High,
}

/// <summary>MP4 ekran kaydı tercihleri.</summary>
public sealed class VideoSettings
{
    public int Fps { get; set; } = 30;
    public VideoQuality Quality { get; set; } = VideoQuality.High;
    public bool CaptureCursor { get; set; } = true;
    public bool HighlightClicks { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public string MicDeviceId { get; set; } = "";
    public bool ShowCountdown { get; set; } = true;
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
        parts.Add(DisplayKey(Key));
        return string.Join(" + ", parts);
    }

    private static string DisplayKey(string key)
    {
        if (key.Length == 2 && (key[0] is 'D' or 'd') && char.IsDigit(key[1]))
            return key[1].ToString();
        return key;
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
    public string FreeBackgroundColor { get; set; } = "#FF1F2430";
    public double StrokeWidth { get; set; } = 4;
    /// <summary>Kullanılmıyor (geriye dönük JSON uyumu). Her zaman 1.0 sayılır; kaydedilmez.</summary>
    public double Opacity { get; set; } = 1.0;

    // Ok başı boyut çarpanı (kalınlıktan bağımsız)
    public double ArrowHeadScale { get; set; } = 1.0;

    // Metin
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 20;
    public bool FontBold { get; set; } = true;
    public bool FontItalic { get; set; } = false;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public bool TextShadow { get; set; } = true;
    public int TextShadowLevel { get; set; } = 1;   // 0=Hafif, 1=Normal, 2=Güçlü
    public bool TextStroke { get; set; } = false;
    public string TextStrokeColor { get; set; } = "#FF000000";
    public bool TextRibbon { get; set; } = true;            // paddingli şerit arka plan
    public string TextRibbonColor { get; set; } = "#CC1F2430";
    public TextAlignmentMode TextAlignment { get; set; } = TextAlignmentMode.Left;

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

public enum TextAlignmentMode
{
    Left,
    Center,
    Right,
}
