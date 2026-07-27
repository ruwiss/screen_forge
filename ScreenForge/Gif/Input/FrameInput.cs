namespace ScreenForge.Gif.Input;

/// <summary>
/// Bir karede olan girdi etkinliği. Kare pikselleriyle birlikte saklanır,
/// böylece dışa aktarımda imleç vurgusu ve tuş rozetleri çizilebilir.
/// </summary>
public sealed class FrameInput
{
    /// <summary>İmlecin yakalama bölgesine göreli konumu.</summary>
    public int CursorX { get; set; }
    public int CursorY { get; set; }

    /// <summary>İmleç bu karede bölge içinde görünür müydü.</summary>
    public bool CursorVisible { get; set; }

    /// <summary>Bu karede basılı olan fare düğmeleri.</summary>
    public MouseButtons Buttons { get; set; }

    /// <summary>Bu karede yeni bir tıklama başladı mı (vurgu animasyonunu tetikler).</summary>
    public bool ClickStarted { get; set; }

    /// <summary>Bu karede aktif olan tuş etiketleri, örn. "Ctrl", "Shift", "S".</summary>
    public List<string> Keys { get; } = new();

    /// <summary>
    /// Kare, üzerine kaplama çizilmesini gerektiren bir olay taşıyor mu.
    /// Yalnızca imlecin görünür olması yeterli değildir; imleç zaten karenin
    /// piksellerine çizilmiştir ve kendi başına kareyi benzersiz kılmaz.
    /// </summary>
    public bool HasAnyInput => Buttons != MouseButtons.None || Keys.Count > 0;
}
