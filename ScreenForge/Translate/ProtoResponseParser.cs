using System.Text;

namespace ScreenForge.Translate;

/// <summary>
/// Structured walker for LensOverlayServerResponse (objects_response text + deep_gleams).
/// Field numbers from Chromium Lens protos (chrome-lens-py).
/// </summary>
internal static class ProtoResponseParser
{
    // LensOverlayServerResponse
    private const int F_ObjectsResponse = 2;
    // ObjectsResponse
    private const int F_Text = 3;
    private const int F_DeepGleams = 4;
    // Text
    private const int F_TextLayout = 1;
    private const int F_ContentLanguage = 2;
    // TextLayout
    private const int F_Paragraphs = 1;
    // Paragraph
    private const int F_ParaLines = 2;
    private const int F_ParaGeometry = 3;
    // Line
    private const int F_Words = 1;
    private const int F_LineGeometry = 2;
    // Word
    private const int F_PlainText = 2;
    private const int F_TextSeparator = 3;
    // Geometry
    private const int F_BoundingBox = 1;
    // CenterRotatedBox
    private const int F_Cx = 1, F_Cy = 2, F_W = 3, F_H = 4, F_Rot = 5, F_CoordType = 6;
    // DeepGleamData
    private const int F_Translation = 10;
    private const int F_VisualObjectId = 11;
    // TranslationData
    private const int F_TrStatus = 1, F_TrTarget = 2, F_TrSource = 3, F_TrText = 4, F_TrLine = 5;
    // Status
    private const int F_StatusCode = 1;

    public const int StatusSuccess = 1;
    public const int StatusSameLanguage = 4;

    public static LensTranslateResult Parse(byte[] data, string? targetLanguage = null)
    {
        var paragraphs = new List<LensTextBlock>();
        var gleams = new List<LensGleamBlock>();
        string? contentLang = null;

        WalkTop(data, paragraphs, gleams, ref contentLang);

        // Match gleams to paragraphs by index (Lens order matches layout order)
        var blocks = new List<LensTextBlock>();
        int n = Math.Max(paragraphs.Count, gleams.Count);
        for (int i = 0; i < n; i++)
        {
            LensTextBlock? p = i < paragraphs.Count ? paragraphs[i] : null;
            bool hasG = i < gleams.Count;
            LensGleamBlock g = hasG ? gleams[i] : default;

            string ocr = p?.OcrText ?? "";
            string? tr = null;
            int status = hasG ? g.StatusCode : 0;
            if (hasG && status == StatusSuccess && !string.IsNullOrWhiteSpace(g.Translation))
                tr = g.Translation;
            else if (hasG && status == StatusSameLanguage)
                tr = ocr; // zaten hedef dilde
            else if (hasG && !string.IsNullOrWhiteSpace(g.Translation))
                tr = g.Translation;

            var box = p?.Box ?? default;
            if (box.Width <= 0 && hasG)
                box = new LensNormBox(0.5f, 0.5f, 0.9f, 0.08f); // fallback

            if (string.IsNullOrWhiteSpace(ocr) && string.IsNullOrWhiteSpace(tr))
                continue;

            bool replace = status == StatusSuccess && !string.IsNullOrWhiteSpace(tr);
            blocks.Add(new LensTextBlock(
                OcrText: ocr,
                TranslatedText: tr ?? ocr,
                Box: box,
                StatusCode: status,
                ShouldReplace: replace));
        }

        // Full strings for toast / fallback
        string fullOcr = string.Join("\n", blocks.Select(b => b.OcrText).Where(s => !string.IsNullOrWhiteSpace(s)));
        string fullTr = string.Join("\n", blocks.Select(b => b.TranslatedText).Where(s => !string.IsNullOrWhiteSpace(s)));

        // Legacy word boxes for older renderer path (paragraph boxes as words)
        var words = blocks
            .Where(b => b.Box.Width > 0)
            .Select(b => new LensWordBox(b.TranslatedText, b.Box.CenterX, b.Box.CenterY, b.Box.Width, b.Box.Height))
            .ToList();

        if (blocks.Count == 0)
        {
            // Last-resort: flat string harvest
            return FallbackFlatParse(data, targetLanguage);
        }

        return new LensTranslateResult
        {
            OcrText = fullOcr,
            TranslatedText = fullTr,
            DetectedLanguage = contentLang,
            Words = words,
            Blocks = blocks,
        };
    }

    private static void WalkTop(
        byte[] data,
        List<LensTextBlock> paragraphs,
        List<LensGleamBlock> gleams,
        ref string? contentLang)
    {
        foreach (var (field, payload) in ReadFields(data, 0, data.Length))
        {
            if (field == F_ObjectsResponse)
                WalkObjects(payload, paragraphs, gleams, ref contentLang);
        }
    }

    private static void WalkObjects(
        ReadOnlySpan<byte> data,
        List<LensTextBlock> paragraphs,
        List<LensGleamBlock> gleams,
        ref string? contentLang)
    {
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == F_Text)
                WalkText(payload, paragraphs, ref contentLang);
            else if (field == F_DeepGleams)
                WalkDeepGleam(payload, gleams);
        }
    }

    private static void WalkText(
        ReadOnlySpan<byte> data,
        List<LensTextBlock> paragraphs,
        ref string? contentLang)
    {
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == F_TextLayout)
                WalkLayout(payload, paragraphs);
            else if (field == F_ContentLanguage && TryUtf8(payload, out var lang))
                contentLang = lang;
        }
    }

    private static void WalkLayout(ReadOnlySpan<byte> data, List<LensTextBlock> paragraphs)
    {
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == F_Paragraphs)
                paragraphs.Add(ParseParagraph(payload));
        }
    }

    private static LensTextBlock ParseParagraph(ReadOnlySpan<byte> data)
    {
        var lineTexts = new List<string>();
        LensNormBox box = default;
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == F_ParaLines)
            {
                string line = ParseLineText(payload);
                if (!string.IsNullOrWhiteSpace(line))
                    lineTexts.Add(line);
                // line geometry fallback if para geometry missing
                var lb = ParseGeometryMessage(payload, F_LineGeometry);
                if (box.Width <= 0 && lb.Width > 0) box = lb;
            }
            else if (field == F_ParaGeometry)
            {
                var pb = ParseBoundingBoxContainer(payload);
                if (pb.Width > 0) box = pb;
            }
        }

        string ocr = string.Join(" ", lineTexts);
        return new LensTextBlock(ocr, ocr, box, 0, false);
    }

    private static string ParseLineText(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field != F_Words) continue;
            string word = "", sep = " ";
            foreach (var (wf, wp) in ReadFields(payload))
            {
                if (wf == F_PlainText && TryUtf8(wp, out var t)) word = t;
                else if (wf == F_TextSeparator && TryUtf8(wp, out var s)) sep = s;
            }
            sb.Append(word);
            sb.Append(sep);
        }
        return sb.ToString().Trim();
    }

    private static LensNormBox ParseGeometryMessage(ReadOnlySpan<byte> data, int geometryField)
    {
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == geometryField)
                return ParseBoundingBoxContainer(payload);
        }
        return default;
    }

    private static LensNormBox ParseBoundingBoxContainer(ReadOnlySpan<byte> data)
    {
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field == F_BoundingBox)
                return ParseBoundingBox(payload);
        }
        return default;
    }

    private static LensNormBox ParseBoundingBox(ReadOnlySpan<byte> data)
    {
        float cx = 0, cy = 0, w = 0, h = 0;
        int i = 0;
        while (i < data.Length)
        {
            if (!TryReadVarint(data, ref i, out ulong tag)) break;
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (wire == 5 && i + 4 <= data.Length)
            {
                float f = BitConverter.ToSingle(data.Slice(i, 4));
                i += 4;
                switch (field)
                {
                    case F_Cx: cx = f; break;
                    case F_Cy: cy = f; break;
                    case F_W: w = f; break;
                    case F_H: h = f; break;
                }
            }
            else if (wire == 0)
            {
                if (!TryReadVarint(data, ref i, out _)) break;
            }
            else if (wire == 2)
            {
                if (!TryReadVarint(data, ref i, out ulong len)) break;
                i += (int)len;
            }
            else if (wire == 1)
            {
                i += 8;
            }
            else break;
        }
        return new LensNormBox(cx, cy, w, h);
    }

    private static void WalkDeepGleam(ReadOnlySpan<byte> data, List<LensGleamBlock> gleams)
    {
        int status = 0;
        string? tr = null;
        string? src = null, tgt = null;
        foreach (var (field, payload) in ReadFields(data))
        {
            if (field != F_Translation) continue;
            foreach (var (tf, tp) in ReadFields(payload))
            {
                if (tf == F_TrStatus)
                    status = ReadFirstVarintField(tp, F_StatusCode);
                else if (tf == F_TrTarget && TryUtf8(tp, out var tg)) tgt = tg;
                else if (tf == F_TrSource && TryUtf8(tp, out var sc)) src = sc;
                else if (tf == F_TrText && TryUtf8(tp, out var text)) tr = text;
            }
        }
        gleams.Add(new LensGleamBlock(status, tr, src, tgt));
    }

    private static int ReadFirstVarintField(ReadOnlySpan<byte> data, int wantField)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (!TryReadVarint(data, ref i, out ulong tag)) break;
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (wire == 0)
            {
                if (!TryReadVarint(data, ref i, out ulong val)) break;
                if (field == wantField) return (int)val;
            }
            else if (wire == 2)
            {
                if (!TryReadVarint(data, ref i, out ulong len)) break;
                i += (int)len;
            }
            else if (wire == 5) i += 4;
            else if (wire == 1) i += 8;
            else break;
        }
        return 0;
    }

    /// <summary>Yield (fieldNumber, payload) for wire type 2 length-delimited fields only.</summary>
    private static List<(int field, byte[] payload)> ReadFields(ReadOnlySpan<byte> data)
        => ReadFields(data.ToArray(), 0, data.Length);

    private static List<(int field, byte[] payload)> ReadFields(byte[] data, int start, int end)
    {
        var list = new List<(int, byte[])>();
        int i = start;
        while (i < end)
        {
            if (!TryReadVarint(data, ref i, end, out ulong tag)) break;
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (wire == 2)
            {
                if (!TryReadVarint(data, ref i, end, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > end) break;
                list.Add((field, data.AsSpan(i, l).ToArray()));
                i += l;
            }
            else if (wire == 0)
            {
                if (!TryReadVarint(data, ref i, end, out _)) break;
            }
            else if (wire == 5)
            {
                if (i + 4 > end) break;
                i += 4;
            }
            else if (wire == 1)
            {
                if (i + 8 > end) break;
                i += 8;
            }
            else break;
        }
        return list;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int i, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (i < data.Length && shift < 64)
        {
            byte b = data[i++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }
        return false;
    }

    private static bool TryReadVarint(byte[] data, ref int i, int end, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (i < end && shift < 64)
        {
            byte b = data[i++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }
        return false;
    }

    private static bool TryUtf8(ReadOnlySpan<byte> span, out string text)
    {
        text = "";
        if (span.Length == 0 || span.Length > 8000) return false;
        foreach (byte b in span)
            if (b == 0) return false;
        try
        {
            text = Encoding.UTF8.GetString(span);
        }
        catch { return false; }
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            return false;
        return text.Length > 0;
    }

    private static LensTranslateResult FallbackFlatParse(byte[] data, string? targetLanguage)
    {
        // Minimal fallback if structured parse fails
        var strings = new List<string>();
        CollectStrings(data, 0, data.Length, strings, 0);
        var natural = strings
            .Where(s => s.Length is >= 2 and <= 2000)
            .Where(s => s.Any(char.IsLetter))
            .Where(s => !s.Contains("bns/") && !s.Contains("dummy", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        string? tr = natural.OrderByDescending(s => s.Count(c => c > 127)).ThenByDescending(s => s.Length).FirstOrDefault();
        return new LensTranslateResult
        {
            OcrText = null,
            TranslatedText = tr,
            Blocks = tr == null
                ? Array.Empty<LensTextBlock>()
                : new[] { new LensTextBlock("", tr, new LensNormBox(0.5f, 0.5f, 0.92f, 0.8f), StatusSuccess, true) },
        };
    }

    private static void CollectStrings(byte[] data, int start, int end, List<string> strings, int depth)
    {
        if (depth > 24) return;
        int i = start;
        while (i < end)
        {
            if (!TryReadVarint(data, ref i, end, out ulong tag)) break;
            int wire = (int)(tag & 7);
            if (wire == 2)
            {
                if (!TryReadVarint(data, ref i, end, out ulong len)) break;
                int l = (int)len;
                if (l < 0 || i + l > end) break;
                var span = data.AsSpan(i, l);
                i += l;
                if (TryUtf8(span, out var s)) strings.Add(s);
                else if (l > 4) CollectStrings(data, i - l, i, strings, depth + 1);
            }
            else if (wire == 0) { if (!TryReadVarint(data, ref i, end, out _)) break; }
            else if (wire == 5) i += 4;
            else if (wire == 1) i += 8;
            else break;
        }
    }
}

internal readonly record struct LensGleamBlock(int StatusCode, string? Translation, string? SourceLang, string? TargetLang);
