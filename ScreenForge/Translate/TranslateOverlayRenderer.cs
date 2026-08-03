using SkiaSharp;

namespace ScreenForge.Translate;

/// <summary>
/// Covers each OCR paragraph box and draws the matching translation (multi-block layout).
/// </summary>
public static class TranslateOverlayRenderer
{
    public static SKBitmap Render(SKBitmap source, LensTranslateResult result)
    {
        var bmp = source.Copy() ?? new SKBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(bmp);
        int w = bmp.Width, h = bmp.Height;

        var blocks = result.Blocks;
        if (blocks.Count == 0)
        {
            // Single full-panel fallback
            string text = result.TranslatedText ?? result.OcrText ?? "";
            if (string.IsNullOrWhiteSpace(text)) return bmp;
            var full = new LensTextBlock(text, text, new LensNormBox(0.5f, 0.5f, 0.94f, 0.9f), 1, true);
            DrawBlock(canvas, bmp, full, w, h);
            return bmp;
        }

        foreach (var block in blocks)
        {
            if (!block.ShouldReplace && string.IsNullOrWhiteSpace(block.TranslatedText))
                continue;
            // SAME_LANGUAGE: leave original pixels
            if (block.StatusCode == ProtoResponseParser.StatusSameLanguage)
                continue;
            if (string.IsNullOrWhiteSpace(block.TranslatedText))
                continue;
            // If translation equals OCR and source looks same, skip redraw noise
            if (string.Equals(block.TranslatedText.Trim(), block.OcrText.Trim(), StringComparison.Ordinal)
                && block.StatusCode != ProtoResponseParser.StatusSuccess)
                continue;

            DrawBlock(canvas, bmp, block, w, h);
        }

        return bmp;
    }

    private static void DrawBlock(SKCanvas canvas, SKBitmap bmp, LensTextBlock block, int imgW, int imgH)
    {
        var rect = ToPixelRect(block.Box, imgW, imgH);
        if (rect.Width < 2 || rect.Height < 2) return;

        // Pad slightly to cover anti-aliased glyphs; slightly larger box for bigger type
        float pad = Math.Max(2, Math.Min(imgW, imgH) * 0.008f);
        rect.Inflate(pad * 1.4f, pad * 1.2f);
        rect.Intersect(SKRect.Create(0, 0, imgW, imgH));

        var bg = SampleBorderColor(bmp, rect);
        using (var fill = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRect(rect, fill);

        bool lightBg = bg.Red * 0.2126 + bg.Green * 0.7152 + bg.Blue * 0.0722 > 140;
        var fg = lightBg ? new SKColor(18, 18, 18) : new SKColor(245, 245, 245);

        float innerPad = Math.Max(2, Math.Min(rect.Width, rect.Height) * 0.04f);
        float maxW = Math.Max(8, rect.Width - innerPad * 2);
        float maxH = Math.Max(8, rect.Height - innerPad * 2);

        string text = block.TranslatedText.Replace("\r\n", "\n").Trim();
        // Biraz daha büyük punto (kutuya sığana kadar)
        float fontSize = FitFontSize(text, maxW, maxH) * 1.22f;
        fontSize = Math.Min(fontSize, maxH * 0.72f);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI")
            ?? SKTypeface.FromFamilyName("Arial")
            ?? SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { Color = fg, IsAntialias = true };

        var lines = WrapLines(text, font, maxW);
        // Hâlâ sığmıyorsa bir kademe küçült
        float lineHeight = fontSize * 1.22f;
        if (lines.Count * lineHeight > maxH && fontSize > 8)
        {
            fontSize = Math.Max(8, fontSize * (maxH / (lines.Count * lineHeight)));
            font.Size = fontSize;
            lines = WrapLines(text, font, maxW);
            lineHeight = fontSize * 1.22f;
        }
        float totalH = lines.Count * lineHeight;
        float y = rect.Top + innerPad + Math.Max(0, (maxH - totalH) / 2f) + fontSize * 0.85f;

        foreach (var line in lines)
        {
            if (y > rect.Bottom - innerPad * 0.5f) break;
            float tw = font.MeasureText(line);
            // Left-align multi-line body text; center only very short lines
            float x = lines.Count == 1 && tw < maxW * 0.6f
                ? rect.Left + innerPad + (maxW - tw) / 2f
                : rect.Left + innerPad;
            canvas.DrawText(line, x, y, font, paint);
            y += lineHeight;
        }
    }

    private static SKRect ToPixelRect(LensNormBox box, int w, int h)
    {
        float x1 = (box.CenterX - box.Width / 2f) * w;
        float y1 = (box.CenterY - box.Height / 2f) * h;
        float bw = box.Width * w;
        float bh = box.Height * h;
        return SKRect.Create(x1, y1, bw, bh);
    }

    private static SKColor SampleBorderColor(SKBitmap bmp, SKRect box)
    {
        int x1 = Math.Clamp((int)box.Left, 0, bmp.Width - 1);
        int y1 = Math.Clamp((int)box.Top, 0, bmp.Height - 1);
        int x2 = Math.Clamp((int)box.Right, 0, bmp.Width - 1);
        int y2 = Math.Clamp((int)box.Bottom, 0, bmp.Height - 1);
        long r = 0, g = 0, b = 0, n = 0;
        void Acc(int x, int y)
        {
            if ((uint)x >= (uint)bmp.Width || (uint)y >= (uint)bmp.Height) return;
            var c = bmp.GetPixel(x, y);
            r += c.Red; g += c.Green; b += c.Blue; n++;
        }
        int stepX = Math.Max(1, (x2 - x1) / 12);
        int stepY = Math.Max(1, (y2 - y1) / 12);
        for (int x = x1; x <= x2; x += stepX)
        {
            Acc(x, Math.Max(0, y1 - 3));
            Acc(x, Math.Min(bmp.Height - 1, y2 + 3));
        }
        for (int y = y1; y <= y2; y += stepY)
        {
            Acc(Math.Max(0, x1 - 3), y);
            Acc(Math.Min(bmp.Width - 1, x2 + 3), y);
        }
        // Also sample a few interior corners of the box (typical UI background)
        Acc(x1 + 2, y1 + 2);
        Acc(x2 - 2, y1 + 2);
        Acc(x1 + 2, y2 - 2);
        Acc(x2 - 2, y2 - 2);
        if (n == 0) return new SKColor(0x1A, 0x1A, 0x1A);
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    private static float FitFontSize(string text, float maxW, float maxH)
    {
        // Estimate lines needed
        int chars = Math.Max(1, text.Length);
        float lo = 8f, hi = Math.Clamp(maxH * 0.72f, 11f, 56f), best = 13f;
        using var tf = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        while (hi - lo > 0.4f)
        {
            float mid = (lo + hi) / 2f;
            using var font = new SKFont(tf, mid);
            var lines = WrapLines(text, font, maxW);
            float needH = lines.Count * mid * 1.28f;
            float maxLineW = 0;
            foreach (var ln in lines)
                maxLineW = Math.Max(maxLineW, font.MeasureText(ln));
            if (needH <= maxH && maxLineW <= maxW * 1.02f)
            {
                best = mid;
                lo = mid;
            }
            else hi = mid;
        }
        return best;
    }

    private static List<string> WrapLines(string text, SKFont font, float maxW)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                result.Add("");
                continue;
            }
            string cur = words[0];
            for (int i = 1; i < words.Length; i++)
            {
                string test = cur + " " + words[i];
                if (font.MeasureText(test) <= maxW) cur = test;
                else
                {
                    result.Add(cur);
                    cur = words[i];
                }
            }
            result.Add(cur);
        }
        return result;
    }
}
