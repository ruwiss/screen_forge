using System.Drawing;

namespace ScreenForge.Subtitle;

internal static class SubtitleMath
{
    public static string TargetLanguage(string? native)
        => string.IsNullOrWhiteSpace(native) ? "tr" : native.Trim();

    public static string? NormalizeOcr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            string collapsed = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (collapsed.Length > 0)
                kept.Add(collapsed);
        }

        if (kept.Count == 0)
            return null;

        string joined = string.Join('\n', kept);
        return joined.Length <= 2000 ? joined : joined[..2000];
    }

    public static bool SameText(string? a, string? b)
        => string.Equals(a, b, StringComparison.Ordinal);

    public const int SampleCount = 48 * 16;
    public const int ChangeThreshold = 28;

    public static void WriteSamples(ReadOnlySpan<byte> bgra, int width, int height, Span<byte> dest)
    {
        dest.Clear();
        if (width < 1 || height < 1 || bgra.Length < width * height * 4 || dest.Length == 0)
            return;

        int cols = 48;
        int rows = 16;
        int stepX = Math.Max(1, width / cols);
        int stepY = Math.Max(1, height / rows);
        int i = 0;
        for (int y = 0; y < height && i < dest.Length; y += stepY)
        {
            int row = y * width * 4;
            for (int x = 0; x < width && i < dest.Length; x += stepX)
            {
                int p = row + x * 4;
                dest[i++] = (byte)((bgra[p] + bgra[p + 1] + bgra[p + 2]) / 3);
            }
        }
    }

    public static int ChangedSamples(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
    {
        int n = Math.Min(previous.Length, current.Length);
        int changed = 0;
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(previous[i] - current[i]) > 28)
                changed++;
        }
        return changed;
    }

    public static bool NearlySame(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        if (a.Length < 6 || b.Length < 6)
            return false;
        if (Math.Abs(a.Length - b.Length) > 3)
            return false;
        return EditDistance(a, b) <= 2;
    }

    private static int EditDistance(string a, string b)
    {
        int n = a.Length;
        int m = b.Length;
        Span<int> prev = stackalloc int[m + 1];
        Span<int> cur = stackalloc int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            var swap = prev;
            prev = cur;
            cur = swap;
        }
        return prev[m];
    }

    public static ulong Fingerprint(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 1 || height < 1 || bgra.Length < width * height * 4)
            return 0;

        int stepX = Math.Max(1, width / 48);
        int stepY = Math.Max(1, height / 16);
        ulong hash = 14695981039346656037;
        hash ^= (ulong)(uint)width;
        hash *= 1099511628211;
        hash ^= (ulong)(uint)height;
        hash *= 1099511628211;

        for (int y = 0; y < height; y += stepY)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x += stepX)
            {
                int i = row + x * 4;
                int lum = bgra[i] + bgra[i + 1] + bgra[i + 2];
                hash ^= (ulong)(uint)lum;
                hash *= 1099511628211;
            }
        }

        return hash;
    }

    public static (int Width, int Height) OcrSize(int width, int height)
    {
        const int maxW = 1280;
        const int maxH = 240;
        if (width <= maxW && height <= maxH)
            return (Math.Max(1, width), Math.Max(1, height));

        double scale = Math.Min(maxW / (double)width, maxH / (double)height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static byte[] ScaleBgra(ReadOnlySpan<byte> src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        if (sw < 1 || sh < 1 || dw < 1 || dh < 1 || src.Length < sw * sh * 4)
            return dst;

        for (int y = 0; y < dh; y++)
        {
            int sy = y * sh / dh;
            int dstRow = y * dw * 4;
            int srcRow = sy * sw * 4;
            for (int x = 0; x < dw; x++)
            {
                int sx = x * sw / dw;
                int si = srcRow + sx * 4;
                int di = dstRow + x * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        return dst;
    }

    public static Rectangle DefaultBox(Rectangle source, Rectangle screen)
    {
        int margin = 8;
        int maxW = Math.Max(40, screen.Width - margin * 2);
        int maxH = Math.Max(28, screen.Height - margin * 2);
        int w = Math.Min(420, maxW);
        int h = Math.Min(56, maxH);
        int x = source.X + (source.Width - w) / 2;
        int y = source.Bottom + 16;
        if (y + h > screen.Bottom - margin)
            y = source.Y - h - 16;
        x = Math.Clamp(x, screen.Left + margin, Math.Max(screen.Left + margin, screen.Right - w - margin));
        y = Math.Clamp(y, screen.Top + margin, Math.Max(screen.Top + margin, screen.Bottom - h - margin));
        return new Rectangle(x, y, w, h);
    }

    public static Rectangle PushOutside(Rectangle box, Rectangle blocked)
    {
        if (box.Width < 1 || box.Height < 1 || blocked.Width < 1 || blocked.Height < 1)
            return box;
        if (!box.IntersectsWith(blocked))
            return box;

        int left = box.Right - blocked.Left;
        int right = blocked.Right - box.Left;
        int up = box.Bottom - blocked.Top;
        int down = blocked.Bottom - box.Top;
        int min = Math.Min(Math.Min(left, right), Math.Min(up, down));
        if (min == left)
            return new Rectangle(box.X - left, box.Y, box.Width, box.Height);
        if (min == right)
            return new Rectangle(box.X + right, box.Y, box.Width, box.Height);
        if (min == up)
            return new Rectangle(box.X, box.Y - up, box.Width, box.Height);
        return new Rectangle(box.X, box.Y + down, box.Width, box.Height);
    }

    public static bool Contains(int x, int y, int left, int top, int width, int height)
        => width > 0 && height > 0 && x >= left && y >= top && x < left + width && y < top + height;
}
