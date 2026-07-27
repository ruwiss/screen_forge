using System.IO;
using DrawingColor = System.Drawing.Color;

namespace ScreenForge.Gif.Editing;

/// <summary>Kaplamanın kare içindeki yerleşimi.</summary>
public enum OverlayPlacement
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>İlerleme göstergesinin biçimi.</summary>
public enum ProgressStyle
{
    /// <summary>Kenar boyunca uzanan dolu çubuk.</summary>
    Bar,

    /// <summary>Geçen süre / kare sayısı yazısı.</summary>
    Text,
}

/// <summary>İlerleme yazısının neyi göstereceği.</summary>
public enum ProgressReadout
{
    /// <summary>Geçen saniye / toplam saniye.</summary>
    Seconds,

    /// <summary>Kare numarası / toplam kare.</summary>
    Frames,

    /// <summary>Tamamlanma yüzdesi.</summary>
    Percent,
}

/// <summary>Sabit metin kaplaması.</summary>
public sealed class CaptionOptions
{
    public bool Enabled { get; init; }
    public string Text { get; init; } = "";
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 28;
    public bool Bold { get; init; } = true;
    public DrawingColor Color { get; init; } = DrawingColor.White;

    /// <summary>Metnin arkasına çizilen şerit; alfa 0 ise çizilmez.</summary>
    public DrawingColor BackgroundColor { get; init; } = DrawingColor.FromArgb(160, 0, 0, 0);

    /// <summary>Metin konturu kalınlığı; 0 ise kontur çizilmez.</summary>
    public double OutlineThickness { get; init; }
    public DrawingColor OutlineColor { get; init; } = DrawingColor.Black;

    public OverlayPlacement Placement { get; init; } = OverlayPlacement.BottomCenter;
    public double Margin { get; init; } = 16;

    public bool HasWork => Enabled && !string.IsNullOrWhiteSpace(Text);
}

/// <summary>İlerleme göstergesi kaplaması.</summary>
public sealed class ProgressOptions
{
    public bool Enabled { get; init; }
    public ProgressStyle Style { get; init; } = ProgressStyle.Bar;
    public ProgressReadout Readout { get; init; } = ProgressReadout.Seconds;

    public DrawingColor Color { get; init; } = DrawingColor.FromArgb(255, 234, 111, 18);

    /// <summary>Çubuğun arkasındaki iz; alfa 0 ise çizilmez.</summary>
    public DrawingColor TrackColor { get; init; } = DrawingColor.FromArgb(90, 0, 0, 0);

    /// <summary>Çubuk kalınlığı (kaynak piksel).</summary>
    public double Thickness { get; init; } = 6;

    /// <summary>Çubuk dikey kenarda mı uzanacak.</summary>
    public bool Vertical { get; init; }

    /// <summary>Çubuğun hangi kenara yaslanacağı. Yazı modunda metnin konumu.</summary>
    public OverlayPlacement Placement { get; init; } = OverlayPlacement.BottomLeft;

    /// <summary>
    /// Saniye gösteriminde ondalık basamak: 0 = tam sayı (3/7 sn),
    /// 1 = salise (3.6/7.2 sn).
    /// </summary>
    public int SecondsDecimals { get; init; } = 1;

    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 14;
    public DrawingColor TextColor { get; init; } = DrawingColor.White;
    public DrawingColor TextBackgroundColor { get; init; } = DrawingColor.FromArgb(170, 0, 0, 0);
    public double Margin { get; init; } = 10;

    public bool HasWork => Enabled;
}

/// <summary>Kare kenarına çizilen çerçeve.</summary>
public sealed class BorderOptions
{
    public bool Enabled { get; init; }
    public DrawingColor Color { get; init; } = DrawingColor.Black;

    /// <summary>Kalınlık (kaynak piksel). İçe doğru çizilir, kare boyutu değişmez.</summary>
    public double Thickness { get; init; } = 2;

    public bool HasWork => Enabled && Thickness > 0 && Color.A > 0;
}

/// <summary>Köşeye yerleştirilen metin ya da logo filigranı.</summary>
public sealed class WatermarkOptions
{
    public bool Enabled { get; init; }
    public string Text { get; init; } = "";
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 16;
    public bool Bold { get; init; }

    /// <summary>Yazı rengi. Alfa saydamlığı belirler.</summary>
    public DrawingColor Color { get; init; } = DrawingColor.FromArgb(140, 255, 255, 255);

    /// <summary>
    /// Logo dosyası. Verilirse metin yerine görsel çizilir.
    /// </summary>
    public string? ImagePath { get; init; }

    /// <summary>Logonun kare genişliğine oranı (0.02-0.5).</summary>
    public double ImageScale { get; init; } = 0.12;

    /// <summary>Logo saydamlığı (0-1).</summary>
    public double ImageOpacity { get; init; } = 0.85;

    public OverlayPlacement Placement { get; init; } = OverlayPlacement.BottomRight;
    public double Margin { get; init; } = 12;

    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);

    public bool HasWork => Enabled
        && (HasImage || (!string.IsNullOrWhiteSpace(Text) && Color.A > 0));
}

/// <summary>Dışa aktarımda karelere çizilecek tüm kaplamalar.</summary>
public sealed class OverlaySet
{
    public CaptionOptions Caption { get; init; } = new();
    public ProgressOptions Progress { get; init; } = new();
    public BorderOptions Border { get; init; } = new();
    public WatermarkOptions Watermark { get; init; } = new();

    public bool HasWork => Caption.HasWork || Progress.HasWork || Border.HasWork || Watermark.HasWork;
}
