using SkiaSharp;

namespace ScreenForge.Presenter;

public static class ArrowBend
{
    public const float Bulge = 0.22f;

    public static SKPoint ControlPoint(SKPoint start, SKPoint end, SKRect screen, bool flip = false)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        var mid = new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f);
        if (len < 4f)
            return mid;

        float px = -dy / len;
        float py = dx / len;
        float offset = len * Bulge;
        var a = new SKPoint(mid.X + px * offset, mid.Y + py * offset);
        var b = new SKPoint(mid.X - px * offset, mid.Y - py * offset);

        SKPoint preferred = MathF.Abs(a.Y - b.Y) >= 1.5f
            ? (a.Y <= b.Y ? a : b)
            : (a.X >= b.X ? a : b);

        var other = preferred == a ? b : a;

        var safe = Inflated(screen, 24);
        SKPoint pick = preferred;
        if (!safe.Contains(preferred.X, preferred.Y) && safe.Contains(other.X, other.Y))
            pick = other;

        return flip ? Opposite(pick, preferred, other) : pick;
    }

    private static SKRect Inflated(SKRect r, float pad) =>
        new(r.Left + pad, r.Top + pad, r.Right - pad, r.Bottom - pad);

    private static SKPoint Opposite(SKPoint pick, SKPoint a, SKPoint b) =>
        pick == a ? b : a;
}
