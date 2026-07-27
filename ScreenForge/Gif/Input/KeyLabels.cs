using System.Windows.Input;

namespace ScreenForge.Gif.Input;

/// <summary>Tuşları ekranda gösterilecek kısa etiketlere çevirir.</summary>
internal static class KeyLabels
{
    /// <summary>
    /// Tuşun rozet etiketi; gösterilmemesi gereken tuşlar için <see langword="null"/>.
    /// </summary>
    public static string? Describe(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt or Key.System => "Alt",
        Key.LWin or Key.RWin => "Win",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Escape => "Esc",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.OemPlus or Key.Add => "+",
        Key.OemMinus or Key.Subtract => "-",
        Key.OemComma => ",",
        Key.OemPeriod or Key.Decimal => ".",
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => key.ToString().TrimStart('D'),
        >= Key.NumPad0 and <= Key.NumPad9 => key.ToString().Replace("NumPad", ""),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        _ => null,
    };

    /// <summary>
    /// Basılı tuşları okunabilir sıraya dizer: değiştiriciler önce, sonra normal tuşlar.
    /// </summary>
    public static IEnumerable<string> Order(IEnumerable<Key> keys)
    {
        var labels = new List<(int rank, string label)>();
        var seen = new HashSet<string>();

        foreach (var key in keys)
        {
            var label = Describe(key);
            if (label == null || !seen.Add(label))
                continue;

            labels.Add((ModifierRank(label), label));
        }

        return labels.OrderBy(x => x.rank).ThenBy(x => x.label, StringComparer.Ordinal).Select(x => x.label);
    }

    private static int ModifierRank(string label) => label switch
    {
        "Ctrl" => 0,
        "Alt" => 1,
        "Shift" => 2,
        "Win" => 3,
        _ => 4,
    };
}
