namespace ScreenForge.Record;

public static class VideoGeometry
{
    public static (int Width, int Height) EvenSize(int width, int height)
    {
        width &= ~1;
        height &= ~1;
        if (width < 2 || height < 2)
            return (0, 0);
        return (width, height);
    }

    public const int MaxLongEdge = 2560;

    public static (int Width, int Height) CapLongEdge(int width, int height, int maxEdge = MaxLongEdge)
    {
        var (w, h) = EvenSize(width, height);
        if (w == 0) return (0, 0);
        int longest = Math.Max(w, h);
        if (longest <= maxEdge) return (w, h);
        double scale = maxEdge / (double)longest;
        return EvenSize((int)Math.Round(w * scale), (int)Math.Round(h * scale));
    }
}
