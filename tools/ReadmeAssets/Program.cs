using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;
using ScreenForge.Editor;

// Offscreen README renders — no real screen capture.
// Usage: dotnet run --project tools/ReadmeAssets -- <outDir>
//
// Only 2 assets:
//   hero.png          region capture + mode bar
//   annotations.png   annotation tools on a realistic UI mock

var outDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "readme"));
Directory.CreateDirectory(outDir);
Console.WriteLine($"Output: {outDir}");

// Brand (Theme.xaml)
var accent = new SKColor(0xEA, 0x6F, 0x12);
var surface = new SKColor(0x1F, 0x24, 0x30);
var surfaceAlt = new SKColor(0x27, 0x2D, 0x3B);
var surfaceHover = new SKColor(0x32, 0x3A, 0x4C);
var border = new SKColor(0x3A, 0x42, 0x54);
var text = new SKColor(0xF2, 0xF4, 0xF8);
var muted = new SKColor(0x9A, 0xA4, 0xB8);
var danger = new SKColor(0xE5, 0x48, 0x4D);
var bgDeep = new SKColor(0x0C, 0x0E, 0x14);
var blue = new SKColor(0x2F, 0x6F, 0xED);
var green = new SKColor(0x3D, 0xB8, 0x6B);
var yellow = new SKColor(0xF2, 0xD6, 0x00);

// Lucide-style paths (24×24 viewBox)
const string PathCursor = "M4 3 L19 11 L11.5 12.5 L9 20 Z";
const string PathArrow = "M5 19 L19 5 M11 5 H19 V13";
const string PathPen = "M3 21 L4 16.5 L16.5 4 A2.1 2.1 0 0 1 20 7 L7.5 19.5 Z M14 6.5 L17.5 10";
const string PathHighlight = "M9 11 L3 17 V21 H8 L12 17 M21 7 L17 3 L11 9 L15 13 Z";
const string PathText = "M5 7 V4 H19 V7 M9 20 H15 M12 4 V20";
const string PathStep = "M12 3 A9 9 0 1 0 12.01 3 Z M10 9 L12 8 V16";
const string PathBlur = "M12 3 C12 3 5 11 5 15 A7 7 0 0 0 19 15 C19 11 12 3 12 3 Z";
const string PathRegion = "M3 3 H7 M3 3 V7 M17 3 H21 M21 3 V7 M3 17 V21 M3 21 H7 M21 17 V21 M17 21 H21 M8 6 H16 M8 18 H16 M6 8 V16 M18 8 V16";
const string PathFullscreen = "M2 4 A2 2 0 0 1 4 2 H20 A2 2 0 0 1 22 4 V16 A2 2 0 0 1 20 18 H4 A2 2 0 0 1 2 16 Z M9 18 L8 22 H16 L15 18";
const string PathLayers = "M4 4 H14 V14 H4 Z M10 10 H20 V20 H10 Z M17 6 V4 M17 8 V6";
const string PathCamera = "M14.5 4 H9.5 L8 6 H5 A2 2 0 0 0 3 8 V18 A2 2 0 0 0 5 20 H19 A2 2 0 0 0 21 18 V8 A2 2 0 0 0 19 6 H16 Z M12 16 A3.5 3.5 0 1 0 12.01 16 Z";

Save("hero.png", RenderHero());
Save("annotations.png", RenderAnnotations());
Console.WriteLine("Done.");

// ===================== Scenes =====================

SKBitmap RenderHero()
{
    // Region capture moment only — mode bar, no export/save panel.
    const int w = 1440, h = 810;
    var bmp = new SKBitmap(w, h);
    using var c = new SKCanvas(bmp);
    FillGradientBg(c, w, h);
    DrawGlow(c, w * 0.78f, h * 0.22f, 280, accent.WithAlpha(28));
    DrawGlow(c, w * 0.18f, h * 0.82f, 240, blue.WithAlpha(22));

    // Quiet brand strip
    DrawAppMark(c, 48, 36, 36);
    DrawText(c, "ScreenForge", 98, 42, 26, text, bold: true);
    DrawText(c, "Yakala · düzenle · paylaş", 98, 74, 14, muted);

    // Desktop shell
    var desk = new SKRect(80, 118, w - 80, h - 56);
    DrawDesktopShell(c, desk);

    // Selection — true horizontal center of canvas
    const float selW = 720, selH = 350;
    float selLeft = (w - selW) / 2f;
    float selTop = 230;
    var sel = new SKRect(selLeft, selTop, selLeft + selW, selTop + selH);
    DrawDimAround(c, new SKRect(0, 0, w, h), sel, 150);
    DrawSelectionFrame(c, sel);
    DrawBadge(c, sel.MidX, sel.Bottom + 18, "720 × 350", center: true);

    // Mode bar — same center X as selection / canvas
    DrawModeBar(c, w / 2f, 158, new[]
    {
        (PathRegion, "Bölge", true),
        (PathFullscreen, "Tam ekran", false),
        (PathLayers, "Serbest", false),
    });

    return bmp;
}

SKBitmap RenderAnnotations()
{
    // Form + annotations share one layout grid — no freehand guess coords.
    // No export/save panel.
    const int w = 1280, h = 720;
    var bmp = new SKBitmap(w, h);
    using var c = new SKCanvas(bmp);
    FillGradientBg(c, w, h);

    var hostOuter = new SKRect(120, 64, w - 88, h - 56);
    var host = new SKRect(hostOuter.Left + 1, hostOuter.Top + 1, hostOuter.Right - 1, hostOuter.Bottom - 1);
    var L = ComputeBugFormLayout(host);

    DrawElevatedCard(c, hostOuter, 16);
    DrawBugReportUi(c, L);

    DrawToolbar(c, hostOuter.Left - 56, hostOuter.MidY, new[]
    {
        (PathCursor, false),
        (PathArrow, true),
        (PathPen, false),
        (PathHighlight, false),
        (PathText, false),
        (PathStep, false),
        (PathBlur, false),
    });

    var scene = new Scene
    {
        CanvasSize = new SKSize(w, h),
        BackgroundColor = SKColors.Transparent,
    };

    // Steps sit left of each field row, vertically centered on the input box
    float stepX = L.ContentL - 36;
    scene.Items.Add(MakeStep(1, stepX, L.KonuBox.MidY));
    scene.Items.Add(MakeStep(2, stepX, L.EmailBox.MidY));
    scene.Items.Add(MakeStep(3, stepX, L.DescBox.MidY));

    // Freehand underline under section title "Yeni ticket"
    float titleW = MeasureText("Yeni ticket", 22, bold: true);
    float penY = L.YTitle + 28;
    var pen = new FreehandItem { StrokeColor = blue, StrokeWidth = 3f };
    pen.ReplacePoints(new[]
    {
        new SKPoint(L.ContentL, penY),
        new SKPoint(L.ContentL + titleW * 0.22f, penY + 3),
        new SKPoint(L.ContentL + titleW * 0.45f, penY - 1),
        new SKPoint(L.ContentL + titleW * 0.68f, penY + 4),
        new SKPoint(L.ContentL + titleW * 0.90f, penY + 1),
        new SKPoint(L.ContentL + titleW, penY + 2),
    });
    scene.Items.Add(pen);

    // Blur sensitive email value
    scene.Items.Add(new BlurItem
    {
        Bounds = InflateRect(L.EmailBox, -6, -8),
        Strength = 10,
        Pixelate = false,
    });
    scene.Items.Add(MakeText("Gizli", L.EmailBox.Left, L.YEmail - 4, muted, 13));

    // Yellow highlight on selected priority chip "Yüksek"
    var hi = L.PrioHigh;
    scene.Items.Add(new HighlightItem
    {
        Points = new List<SKPoint>
        {
            new(hi.Left + 4, hi.MidY),
            new(hi.Left + hi.Width * 0.35f, hi.MidY - 2),
            new(hi.Left + hi.Width * 0.65f, hi.MidY + 1),
            new(hi.Right - 4, hi.MidY),
        },
        StrokeColor = yellow,
        StrokeWidth = 18,
        Opacity = 0.72f,
    });

    // Arrow → validation error inside description box
    float errX = L.DescBox.Left + 18;
    float errY = L.DescBox.Top + 22;
    scene.Items.Add(new ArrowItem
    {
        Start = new SKPoint(L.DescBox.Right + 48, errY + 6),
        End = new SKPoint(errX + 210, errY + 4),
        StrokeColor = accent,
        StrokeWidth = 3.6f,
        HeadScale = 1.1f,
    });
    scene.Items.Add(MakeText("Hata burada", L.DescBox.Right + 20, errY + 18, accent, 16));

    // Red ellipse around primary submit button
    scene.Items.Add(new EllipseItem
    {
        Bounds = InflateRect(L.Btn, 14, 10),
        StrokeColor = danger,
        StrokeWidth = 3.2f,
        FillColor = SKColors.Transparent,
    });

    using var overlay = SceneRenderer.RenderToBitmap(scene);
    using var img = SKImage.FromBitmap(overlay);
    c.DrawImage(img, 0, 0);

    return bmp;
}

BugFormLayout ComputeBugFormLayout(SKRect host)
{
    // Vertical rhythm: title bar 48 → section → fields with fixed gaps.
    float left = host.Left + 48;
    float fieldW = host.Width - 96;

    float yTitle = host.Top + 72;
    float ySubtitle = host.Top + 102;

    // Field tops (label y). Box sits at y+22…y+66 (DrawField).
    float yKonu = host.Top + 142;
    float yEmail = host.Top + 228;
    float yPrio = host.Top + 318;
    float yDesc = host.Top + 400;
    float yBtn = host.Top + 536;

    var konuBox = new SKRect(left, yKonu + 22, left + fieldW, yKonu + 66);
    float emailW = fieldW * 0.55f;
    var emailBox = new SKRect(left, yEmail + 22, left + emailW, yEmail + 66);

    float sideL = left + fieldW * 0.62f;
    var sideCard = new SKRect(sideL, yEmail + 22, left + fieldW, yEmail + 66 + 28);

    // Priority chips under "Öncelik" label
    float chipY = yPrio + 24;
    const float chipW = 96, chipH = 36, chipGap = 12;
    var prioLow = new SKRect(left, chipY, left + chipW, chipY + chipH);
    var prioMid = new SKRect(left + chipW + chipGap, chipY, left + 2 * chipW + chipGap, chipY + chipH);
    var prioHigh = new SKRect(left + 2 * (chipW + chipGap), chipY, left + 3 * chipW + 2 * chipGap, chipY + chipH);

    // Description textarea
    var descBox = new SKRect(left, yDesc + 22, left + fieldW, yDesc + 98);

    // Submit right-aligned under form
    const float btnW = 180, btnH = 48;
    var btn = new SKRect(left + fieldW - btnW, yBtn, left + fieldW, yBtn + btnH);

    return new BugFormLayout(
        host, left, fieldW,
        yTitle, ySubtitle,
        yKonu, yEmail, yPrio, yDesc, yBtn,
        konuBox, emailBox, sideCard,
        prioLow, prioMid, prioHigh,
        descBox, btn);
}

SKRect InflateRect(SKRect r, float dx, float dy) =>
    new(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

// ===================== Drawing primitives =====================

void FillGradientBg(SKCanvas c, int w, int h)
{
    using var paint = new SKPaint
    {
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w, h),
            new[] { bgDeep, new SKColor(0x12, 0x16, 0x20), bgDeep },
            null, SKShaderTileMode.Clamp),
    };
    c.DrawRect(0, 0, w, h, paint);
}

void DrawGlow(SKCanvas c, float x, float y, float r, SKColor color)
{
    using var p = new SKPaint
    {
        IsAntialias = true,
        Color = color,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.45f),
    };
    c.DrawCircle(x, y, r * 0.55f, p);
}

void DrawElevatedCard(SKCanvas c, SKRect r, float radius = 14)
{
    using var shadow = new SKPaint
    {
        Color = new SKColor(0, 0, 0, 90),
        IsAntialias = true,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18),
    };
    c.DrawRoundRect(new SKRect(r.Left + 3, r.Top + 8, r.Right + 3, r.Bottom + 8), radius, radius, shadow);
    using var bg = new SKPaint { Color = surface, IsAntialias = true };
    using var bd = new SKPaint { Style = SKPaintStyle.Stroke, Color = border, StrokeWidth = 1, IsAntialias = true };
    c.DrawRoundRect(r, radius, radius, bg);
    c.DrawRoundRect(r, radius, radius, bd);
}

void DrawAppMark(SKCanvas c, float x, float y, float size)
{
    using var bg = new SKPaint { Color = accent, IsAntialias = true };
    c.DrawRoundRect(new SKRect(x, y, x + size, y + size), size * 0.22f, size * 0.22f, bg);
    DrawIcon(c, x + size * 0.18f, y + size * 0.18f, size * 0.64f, PathCamera, SKColors.White, 2.1f);
}

void DrawIcon(SKCanvas c, float x, float y, float size, string pathData, SKColor color, float stroke = 2f)
{
    if (string.IsNullOrWhiteSpace(pathData)) return;
    using var path = ParseSvgPath(pathData);
    if (path == null) return;

    float scale = size / 24f;
    c.Save();
    c.Translate(x, y);
    c.Scale(scale);
    using var paint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = color,
        StrokeWidth = stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };
    c.DrawPath(path, paint);
    c.Restore();
}

void DrawText(SKCanvas c, string msg, float x, float y, float size, SKColor color, bool bold = false)
{
    var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
    using var typeface = SKTypeface.FromFamilyName("Segoe UI", style)
        ?? SKTypeface.FromFamilyName("Segoe UI Variable Text", style)
        ?? SKTypeface.Default;
    using var font = new SKFont(typeface, size);
    using var paint = new SKPaint { Color = color, IsAntialias = true };
    // y = top of text box; convert to baseline
    c.DrawText(msg, x, y + size * 0.82f, SKTextAlign.Left, font, paint);
}

float MeasureText(string msg, float size, bool bold = false)
{
    var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
    using var typeface = SKTypeface.FromFamilyName("Segoe UI", style) ?? SKTypeface.Default;
    using var font = new SKFont(typeface, size);
    return font.MeasureText(msg);
}

void DrawDesktopShell(SKCanvas c, SKRect desk)
{
    using var shell = new SKPaint { Color = new SKColor(0x18, 0x1E, 0x2A), IsAntialias = true };
    c.DrawRoundRect(desk, 16, 16, shell);

    DrawMiniWindow(c, desk.Left + 60, desk.Top + 48, 320, 200, "Explorer", blue);
    DrawMiniWindow(c, desk.Left + 420, desk.Top + 70, 380, 220, "Browser", accent);
    DrawMiniWindow(c, desk.Left + 840, desk.Top + 56, 280, 180, "Terminal", green);
}

void DrawMiniWindow(SKCanvas c, float x, float y, float w, float h, string title, SKColor accentDot)
{
    using var body = new SKPaint { Color = surfaceAlt, IsAntialias = true };
    using var bd = new SKPaint { Style = SKPaintStyle.Stroke, Color = border, StrokeWidth = 1, IsAntialias = true };
    var r = new SKRect(x, y, x + w, y + h);
    c.DrawRoundRect(r, 10, 10, body);
    c.DrawRoundRect(r, 10, 10, bd);

    using var titleBar = new SKPaint { Color = surface, IsAntialias = true };
    c.DrawRoundRect(new SKRect(x, y, x + w, y + 32), 10, 10, titleBar);
    c.DrawRect(new SKRect(x, y + 16, x + w, y + 32), titleBar);

    using var dot = new SKPaint { Color = accentDot, IsAntialias = true };
    c.DrawCircle(x + 16, y + 16, 4.5f, dot);
    DrawText(c, title, x + 30, y + 8, 12, muted);

    using var line = new SKPaint
    {
        Color = border,
        StrokeWidth = 4,
        StrokeCap = SKStrokeCap.Round,
        IsAntialias = true,
    };
    for (int i = 0; i < 3; i++)
    {
        float ly = y + 56 + i * 28;
        float end = x + w - 28 - i * 18;
        c.DrawLine(x + 20, ly, Math.Max(x + 40, end), ly, line);
    }
}

/// <summary>
/// Realistic “Hata bildir” form — draws from shared BugFormLayout.
/// </summary>
void DrawBugReportUi(SKCanvas c, BugFormLayout L)
{
    var host = L.Host;
    using var bg = new SKPaint { Color = new SKColor(0x1A, 0x20, 0x2C), IsAntialias = true };
    c.DrawRoundRect(host, 14, 14, bg);

    // Title bar
    using var titleBar = new SKPaint { Color = surface, IsAntialias = true };
    c.DrawRoundRect(new SKRect(host.Left, host.Top, host.Right, host.Top + 48), 14, 14, titleBar);
    c.DrawRect(new SKRect(host.Left, host.Top + 24, host.Right, host.Top + 48), titleBar);
    DrawText(c, "Hata bildir", host.Left + 24, host.Top + 14, 16, text, bold: true);
    DrawText(c, "Support · Ticket #4821", host.Right - 210, host.Top + 16, 12, muted);

    // Section title
    DrawText(c, "Yeni ticket", L.ContentL, L.YTitle, 22, text, bold: true);
    DrawText(c, "Kullanıcı panosu · hata raporu formu", L.ContentL, L.YSubtitle, 13, muted);

    // Field 1 — Konu
    DrawField(c, L.ContentL, L.YKonu, L.FieldW, "Konu", "Giriş sonrası yönlendirme hatası");

    // Field 2 — E-posta (blurred by annotation layer)
    DrawField(c, L.ContentL, L.YEmail, L.EmailBox.Width, "E-posta", "kullanici@ornek.com");

    // Side status card (right of email)
    using (var card = new SKPaint { Color = surfaceAlt, IsAntialias = true })
        c.DrawRoundRect(L.SideCard, 12, 12, card);
    DrawText(c, "Durum", L.SideCard.Left + 16, L.SideCard.Top + 12, 11, muted);
    DrawText(c, "Açık · 2 yorum", L.SideCard.Left + 16, L.SideCard.Top + 34, 14, text, bold: true);
    DrawText(c, "SLA 4s", L.SideCard.Left + 16, L.SideCard.Top + 56, 12, green);

    // Field 3 — Öncelik chips
    DrawText(c, "Öncelik", L.ContentL, L.YPrio, 12, muted);
    DrawPrioChip(c, L.PrioLow, "Düşük", false);
    DrawPrioChip(c, L.PrioMid, "Orta", false);
    DrawPrioChip(c, L.PrioHigh, "Yüksek", true);

    // Field 4 — Açıklama + validation error
    DrawText(c, "Açıklama", L.ContentL, L.YDesc, 12, muted);
    using (var box = new SKPaint { Color = surfaceAlt, IsAntialias = true })
    using (var bd = new SKPaint { Style = SKPaintStyle.Stroke, Color = danger, StrokeWidth = 1.5f, IsAntialias = true })
    {
        c.DrawRoundRect(L.DescBox, 10, 10, box);
        c.DrawRoundRect(L.DescBox, 10, 10, bd);
    }
    DrawText(c, "Zorunlu alan boş bırakılamaz.", L.DescBox.Left + 14, L.DescBox.Top + 14, 13, danger);

    // Submit
    using (var btn = new SKPaint { Color = accent, IsAntialias = true })
        c.DrawRoundRect(L.Btn, 10, 10, btn);
    float gonderW = MeasureText("Gönder", 15, bold: true);
    DrawText(c, "Gönder", L.Btn.MidX - gonderW / 2f, L.Btn.Top + 14, 15, SKColors.White, bold: true);
}

void DrawPrioChip(SKCanvas c, SKRect cr, string label, bool on)
{
    using var chip = new SKPaint { Color = on ? accent : surfaceAlt, IsAntialias = true };
    c.DrawRoundRect(cr, 8, 8, chip);
    float tw = MeasureText(label, 13, bold: on);
    DrawText(c, label, cr.MidX - tw / 2f, cr.Top + 10, 13, on ? SKColors.White : muted, bold: on);
}

void DrawField(SKCanvas c, float x, float y, float w, string label, string value)
{
    DrawText(c, label, x, y, 12, muted);
    using var box = new SKPaint { Color = surfaceAlt, IsAntialias = true };
    using var bd = new SKPaint { Style = SKPaintStyle.Stroke, Color = border, StrokeWidth = 1, IsAntialias = true };
    var r = new SKRect(x, y + 22, x + w, y + 66);
    c.DrawRoundRect(r, 10, 10, box);
    c.DrawRoundRect(r, 10, 10, bd);
    DrawText(c, value, x + 14, y + 34, 14, text);
}

void DrawDimAround(SKCanvas c, SKRect full, SKRect hole, byte alpha)
{
    using var dim = new SKPaint { Color = new SKColor(0, 0, 0, alpha) };
    c.DrawRect(new SKRect(full.Left, full.Top, full.Right, hole.Top), dim);
    c.DrawRect(new SKRect(full.Left, hole.Bottom, full.Right, full.Bottom), dim);
    c.DrawRect(new SKRect(full.Left, hole.Top, hole.Left, hole.Bottom), dim);
    c.DrawRect(new SKRect(hole.Right, hole.Top, full.Right, hole.Bottom), dim);
}

void DrawSelectionFrame(SKCanvas c, SKRect r)
{
    using var stroke = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = SKColors.White,
        StrokeWidth = 1.8f,
        PathEffect = SKPathEffect.CreateDash(new[] { 8f, 5f }, 0),
        IsAntialias = true,
    };
    c.DrawRect(r, stroke);
    DrawHandles(c, r, 9);
}

void DrawHandles(SKCanvas c, SKRect r, float s)
{
    SKPoint[] pts =
    {
        new(r.Left, r.Top), new(r.MidX, r.Top), new(r.Right, r.Top),
        new(r.Right, r.MidY), new(r.Right, r.Bottom), new(r.MidX, r.Bottom),
        new(r.Left, r.Bottom), new(r.Left, r.MidY),
    };
    using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
    using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = accent, StrokeWidth = 1.6f, IsAntialias = true };
    foreach (var p in pts)
    {
        var hr = new SKRect(p.X - s / 2, p.Y - s / 2, p.X + s / 2, p.Y + s / 2);
        c.DrawRoundRect(hr, 2, 2, fill);
        c.DrawRoundRect(hr, 2, 2, stroke);
    }
}

void DrawModeBar(SKCanvas c, float centerX, float y, (string path, string label, bool on)[] modes)
{
    // Equal-width chips, bar truly centered on centerX.
    const float chipH = 40f, gap = 8f, pad = 6f, iconSize = 18f, fontSize = 13f;
    float[] chipWs = new float[modes.Length];
    float totalChips = 0;
    for (int i = 0; i < modes.Length; i++)
    {
        float tw = MeasureText(modes[i].label, fontSize, bold: modes[i].on);
        // icon + gap + text + horizontal padding
        chipWs[i] = Math.Max(110f, 12 + iconSize + 8 + tw + 16);
        totalChips += chipWs[i];
    }
    float total = totalChips + (modes.Length - 1) * gap + pad * 2;
    float barLeft = centerX - total / 2f;
    float barTop = y - chipH / 2f - pad;
    DrawGlassBar(c, new SKRect(barLeft, barTop, barLeft + total, y + chipH / 2f + pad));

    float cx = barLeft + pad;
    for (int i = 0; i < modes.Length; i++)
    {
        var (path, label, on) = modes[i];
        float cw = chipWs[i];
        if (on)
        {
            using var hi = new SKPaint { Color = accent, IsAntialias = true };
            c.DrawRoundRect(new SKRect(cx, y - chipH / 2f, cx + cw, y + chipH / 2f), 8, 8, hi);
        }
        var iconCol = on ? SKColors.White : muted;
        float iconX = cx + 12;
        float iconY = y - iconSize / 2f;
        DrawIcon(c, iconX, iconY, iconSize, path, iconCol, 1.9f);

        float textX = iconX + iconSize + 8;
        float textY = y - fontSize / 2f;
        DrawText(c, label, textX, textY, fontSize, on ? SKColors.White : muted, bold: on);
        cx += cw + gap;
    }
}

void DrawToolbar(SKCanvas c, float left, float midY, (string path, bool on)[] tools)
{
    float itemH = 40, pad = 8, w = 48;
    float h = tools.Length * itemH + pad * 2;
    float x = left;
    float y = midY - h / 2f;
    DrawGlassBar(c, new SKRect(x, y, x + w, y + h));

    float iy = y + pad;
    foreach (var (path, on) in tools)
    {
        if (on)
        {
            using var hi = new SKPaint { Color = accent.WithAlpha(55), IsAntialias = true };
            c.DrawRoundRect(new SKRect(x + 5, iy, x + w - 5, iy + itemH - 4), 8, 8, hi);
        }
        DrawIcon(c, x + 14, iy + 8, 20, path, on ? accent : text, 2f);
        iy += itemH;
    }
}

void DrawGlassBar(SKCanvas c, SKRect r)
{
    using var bg = new SKPaint { Color = surface.WithAlpha(0xF0), IsAntialias = true };
    using var bd = new SKPaint { Style = SKPaintStyle.Stroke, Color = border, StrokeWidth = 1, IsAntialias = true };
    c.DrawRoundRect(r, 12, 12, bg);
    c.DrawRoundRect(r, 12, 12, bd);
}

void DrawBadge(SKCanvas c, float x, float y, string value, bool center = false)
{
    float tw = MeasureText(value, 12);
    float left = center ? x - tw / 2f - 12 : x;
    using var bg = new SKPaint { Color = surface.WithAlpha(0xE8), IsAntialias = true };
    c.DrawRoundRect(new SKRect(left, y - 12, left + tw + 24, y + 14), 6, 6, bg);
    DrawText(c, value, left + 12, y - 7, 12, SKColors.White);
}

// ===================== Scene helpers =====================

StepItem MakeStep(int n, float x, float y)
{
    var s = new StepItem
    {
        Number = n,
        Position = new SKPoint(x, y),
        Diameter = 36,
        BadgeColor = danger,
        NumberColor = SKColors.White,
    };
    s.SyncBounds();
    return s;
}

TextItem MakeText(string t, float x, float y, SKColor color, float size = 20)
{
    var item = new TextItem
    {
        Text = t,
        Position = new SKPoint(x, y),
        FontSize = size,
        StrokeColor = color,
        Ribbon = true,
        RibbonColor = new SKColor(0x1F, 0x24, 0x30, 0xCC),
        Shadow = false,
        Bold = true,
    };
    item.Measure();
    return item;
}

// ===================== I/O =====================

void Save(string name, SKBitmap bmp)
{
    var path = Path.Combine(outDir, name);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
    data.SaveTo(fs);
    Console.WriteLine($"  {name}  ({bmp.Width}×{bmp.Height})");
    bmp.Dispose();
}

SKPath? ParseSvgPath(string d)
{
    var path = new SKPath();
    var tokens = Regex.Matches(d, @"([MmLlHhVvAaCcZz])|(-?\d*\.?\d+(?:[eE][+-]?\d+)?)");
    char cmd = 'M';
    float cx = 0, cy = 0, sx = 0, sy = 0;
    int i = 0;
    float Next() => float.Parse(tokens[i++].Value, System.Globalization.CultureInfo.InvariantCulture);
    bool HasNum() => i < tokens.Count && tokens[i].Value[0] is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z');

    while (i < tokens.Count)
    {
        var t = tokens[i].Value;
        if (t.Length == 1 && char.IsLetter(t[0]))
        {
            cmd = t[0];
            i++;
            if (cmd is 'Z' or 'z')
            {
                path.Close();
                cx = sx; cy = sy;
            }
            continue;
        }

        switch (cmd)
        {
            case 'M':
            case 'm':
            {
                float x = Next(), y = Next();
                if (cmd == 'm') { x += cx; y += cy; }
                path.MoveTo(x, y);
                cx = sx = x; cy = sy = y;
                cmd = cmd == 'M' ? 'L' : 'l';
                break;
            }
            case 'L':
            case 'l':
            {
                float x = Next(), y = Next();
                if (cmd == 'l') { x += cx; y += cy; }
                path.LineTo(x, y);
                cx = x; cy = y;
                break;
            }
            case 'H':
            case 'h':
            {
                float x = Next();
                if (cmd == 'h') x += cx;
                path.LineTo(x, cy);
                cx = x;
                break;
            }
            case 'V':
            case 'v':
            {
                float y = Next();
                if (cmd == 'v') y += cy;
                path.LineTo(cx, y);
                cy = y;
                break;
            }
            case 'C':
            case 'c':
            {
                float x1 = Next(), y1 = Next(), x2 = Next(), y2 = Next(), x = Next(), y = Next();
                if (cmd == 'c') { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                path.CubicTo(x1, y1, x2, y2, x, y);
                cx = x; cy = y;
                break;
            }
            case 'A':
            case 'a':
            {
                float rx = Next(), ry = Next(), rot = Next(), large = Next(), sweep = Next(), x = Next(), y = Next();
                if (cmd == 'a') { x += cx; y += cy; }
                path.ArcTo(rx, ry, rot, large >= 0.5f ? SKPathArcSize.Large : SKPathArcSize.Small,
                    sweep >= 0.5f ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise, x, y);
                cx = x; cy = y;
                break;
            }
            default:
                if (HasNum()) { Next(); }
                else i++;
                break;
        }
    }
    return path;
}

/// <summary>Shared form metrics — UI draw + annotation coords use the same grid.</summary>
readonly record struct BugFormLayout(
    SKRect Host,
    float ContentL,
    float FieldW,
    float YTitle,
    float YSubtitle,
    float YKonu,
    float YEmail,
    float YPrio,
    float YDesc,
    float YBtn,
    SKRect KonuBox,
    SKRect EmailBox,
    SKRect SideCard,
    SKRect PrioLow,
    SKRect PrioMid,
    SKRect PrioHigh,
    SKRect DescBox,
    SKRect Btn);
