namespace ScreenForge.Translate;

/// <summary>
/// Picks the second-pass target for Spotlight translate.
/// First call is always auto → native. If the source is already native
/// (or the first result is an identity with no reported source), translate to pair.
/// </summary>
internal static class TranslateLanguageRouter
{
    public static bool ShouldTranslateToPair(
        string nativeLanguage,
        string sourceLang,
        string originalText,
        string firstTranslation)
    {
        if (LanguagesEqual(sourceLang, nativeLanguage))
            return true;

        return string.IsNullOrWhiteSpace(sourceLang)
            && IsIdentity(originalText, firstTranslation);
    }

    internal static bool LanguagesEqual(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        a = a.Trim();
        b = b.Trim();
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;

        string a2 = PrimaryCode(a);
        string b2 = PrimaryCode(b);
        return a2.Equals(b2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Görüntü çevirisi: kaynak her zaman otomatik, hedef her zaman ana dil.
    /// Arapça, İngilizce, fark etmez — ayardaki ana dile gider.
    /// </summary>
    public static (string Target, string? Source) ImageRoute(string? nativeLanguage)
        => (string.IsNullOrWhiteSpace(nativeLanguage) ? "tr" : nativeLanguage.Trim(), null);

    private static bool IsIdentity(string original, string translated)
        => original.Trim().Equals(translated.Trim(), StringComparison.Ordinal);

    private static string PrimaryCode(string code)
    {
        int dash = code.IndexOf('-');
        return dash > 0 ? code[..dash] : code;
    }
}
