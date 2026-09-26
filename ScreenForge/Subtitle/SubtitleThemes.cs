using ScreenForge.Settings;

namespace ScreenForge.Subtitle;

internal sealed record SubtitleTheme(
    string Id,
    string Name,
    bool Light,
    string Back,
    string Text,
    string Border,
    double Opacity,
    double BorderThickness)
{
    public void Apply(SubtitleSettings settings)
    {
        settings.Theme = Id;
        settings.BackColor = Back;
        settings.TextColor = Text;
        settings.BorderColor = Border;
        settings.BorderThickness = BorderThickness;
    }
}

internal static class SubtitleThemes
{
    public static IReadOnlyList<SubtitleTheme> All { get; } =
    [
        new("siyah", "Siyah", false, "#000000", "#FFFFFF", "#FFFFFF", 0.9, 0),
        new("sari", "Sarı", false, "#000000", "#FFE14A", "#FFE14A", 0.9, 0),
        new("cyan", "Cyan", false, "#000000", "#5CFFF0", "#5CFFF0", 0.9, 0),
        new("yesil", "Yeşil", false, "#000000", "#7DFF6B", "#7DFF6B", 0.9, 0),
        new("beyaz", "Beyaz", true, "#FFFFFF", "#111111", "#111111", 0.94, 0),
        new("kutu-sari", "Sarı kutu", true, "#FFE14A", "#111111", "#111111", 0.96, 0),
        new("gece", "Gece", false, "#12141A", "#FFFFFF", "#FFFFFF", 0.82, 0),
        new("mavi", "Mavi", false, "#071426", "#FFFFFF", "#7EB6FF", 0.88, 1),
        new("turuncu", "Turuncu", false, "#000000", "#FFB020", "#FFB020", 0.9, 0),
        new("pembe", "Pembe", false, "#000000", "#FF8AD4", "#FF8AD4", 0.9, 0),
        new("kagit", "Kağıt", true, "#FFFFFF", "#0B1F4A", "#0B1F4A", 0.94, 0),
        new("limon", "Limon", true, "#FFF36A", "#111111", "#111111", 0.96, 0),
        new("kirmizi", "Kırmızı", false, "#000000", "#FF5A5A", "#FF5A5A", 0.9, 0),
        new("mor", "Mor", false, "#000000", "#D2A6FF", "#D2A6FF", 0.9, 0),
        new("nane", "Nane", true, "#C8FFD4", "#062014", "#062014", 0.96, 0),
        new("lacivert", "Lacivert", false, "#0A1630", "#FFFFFF", "#7EB6FF", 0.9, 1),
    ];
}
