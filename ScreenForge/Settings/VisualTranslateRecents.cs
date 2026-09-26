namespace ScreenForge.Settings;

/// <summary>
/// Görsel çeviri menüsü: varsayılan her zaman ayarlardaki çevrilecek dildir.
/// Menüden seçilen başka diller yalnızca son kullanılanlara yazılır.
/// </summary>
internal static class VisualTranslateRecents
{
    internal const int Max = 5;

    internal static string DefaultCode(AppSettings settings)
    {
        string code = (settings.TranslatePairLanguage ?? "").Trim().ToLowerInvariant();
        if (TranslateLanguageDefaults.IsKnown(code))
            return code;
        return TranslateLanguageDefaults.DefaultPair(settings.TranslateNativeLanguage);
    }

    internal static void Remember(AppSettings settings, string code)
    {
        code = code.Trim().ToLowerInvariant();
        settings.RecentVisualLanguages ??= [];
        string def = DefaultCode(settings);
        settings.RecentVisualLanguages.RemoveAll(c =>
            string.Equals(c, code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c, def, StringComparison.OrdinalIgnoreCase)
            || !TranslateLanguageDefaults.IsKnown(c));

        if (!string.Equals(code, def, StringComparison.OrdinalIgnoreCase)
            && TranslateLanguageDefaults.IsKnown(code))
            settings.RecentVisualLanguages.Insert(0, code);

        if (settings.RecentVisualLanguages.Count > Max)
            settings.RecentVisualLanguages.RemoveRange(Max, settings.RecentVisualLanguages.Count - Max);
    }

    internal static IReadOnlyList<string> Visible(AppSettings settings)
    {
        settings.RecentVisualLanguages ??= [];
        string def = DefaultCode(settings);
        var list = new List<string>();
        foreach (var code in settings.RecentVisualLanguages)
        {
            string c = code.Trim().ToLowerInvariant();
            if (!TranslateLanguageDefaults.IsKnown(c))
                continue;
            if (string.Equals(c, def, StringComparison.OrdinalIgnoreCase))
                continue;
            if (list.Contains(c))
                continue;
            list.Add(c);
            if (list.Count == Max)
                break;
        }
        return list;
    }
}
