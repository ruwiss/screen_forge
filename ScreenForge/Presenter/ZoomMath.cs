namespace ScreenForge.Presenter;

public static class ZoomMath
{
    public static void OffsetsKeepingPoint(float mag, float focusX, float focusY, out int xOffset, out int yOffset)
    {
        if (mag <= 1.001f)
        {
            xOffset = 0;
            yOffset = 0;
            return;
        }

        xOffset = (int)MathF.Round(focusX * (1f - 1f / mag));
        yOffset = (int)MathF.Round(focusY * (1f - 1f / mag));
    }

    public static void ClampOffsets(
        float mag,
        int screenX, int screenY, int screenW, int screenH,
        ref int xOffset, ref int yOffset)
    {
        if (mag <= 1.001f || screenW <= 0 || screenH <= 0)
        {
            xOffset = screenX;
            yOffset = screenY;
            return;
        }

        float visW = screenW / mag;
        float visH = screenH / mag;
        int maxX = screenX + Math.Max(0, (int)MathF.Round(screenW - visW));
        int maxY = screenY + Math.Max(0, (int)MathF.Round(screenH - visH));
        xOffset = Math.Clamp(xOffset, screenX, Math.Max(screenX, maxX));
        yOffset = Math.Clamp(yOffset, screenY, Math.Max(screenY, maxY));
    }

    public static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
