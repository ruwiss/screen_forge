using System.Globalization;

namespace ScreenForge.Settings;

/// <summary>Known UI language list plus first-run native/pair defaults.</summary>
internal static class TranslateLanguageDefaults
{
    internal static readonly (string Code, string Label)[] Languages =
    [
        ("tr", "Türkçe"),
        ("en", "English"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("it", "Italiano"),
        ("pt", "Português"),
        ("ru", "Русский"),
        ("ar", "العربية"),
        ("zh", "中文"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("nl", "Nederlands"),
        ("pl", "Polski"),
        ("uk", "Українська"),
        ("hi", "हिन्दी"),
    ];

    internal static bool IsKnown(string code)
    {
        code = code.Trim().ToLowerInvariant();
        foreach (var (c, _) in Languages)
        {
            if (c == code)
                return true;
        }
        return false;
    }

    internal static string MapUiCulture(CultureInfo culture)
    {
        string two = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return IsKnown(two) ? two : "en";
    }

    internal static string DefaultPair(string nativeLanguage)
        => string.Equals(nativeLanguage.Trim(), "en", StringComparison.OrdinalIgnoreCase)
            ? "tr"
            : "en";
}
