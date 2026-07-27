using System.Runtime.InteropServices;

namespace ScreenForge.Gif.Input;

/// <summary>Bir karede yakalanan imleç bilgisi.</summary>
public readonly record struct CursorSnapshot(int X, int Y, bool Visible)
{
    public static readonly CursorSnapshot Hidden = new(0, 0, false);
}

/// <summary>
/// Sistem imlecini yakalar ve bir cihaz bağlamına çizer.
/// ScreenToGif'in <c>ImageCapture.CaptureWithCursor</c> yaklaşımı: imleç ekran
/// yakalamasına dahil değildir, bu yüzden ayrıca <c>DrawIconEx</c> ile çizilir.
/// </summary>
/// <remarks>
/// Animasyonlu imleçler (bekleme çemberi gibi) kare adımı ister; <see cref="_step"/>
/// bunu izler ve geçersiz adımda başa sarar.
/// </remarks>
internal sealed class CursorCapture
{
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
        int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private int _step;

    /// <summary>
    /// İmleci <paramref name="hdc"/> üzerine, <paramref name="originX"/>/<paramref name="originY"/>
    /// ile verilen yakalama bölgesine göreli olarak çizer.
    /// </summary>
    /// <returns>İmlecin bölgeye göreli konumu; görünmüyorsa <see cref="CursorSnapshot.Hidden"/>.</returns>
    public CursorSnapshot DrawInto(IntPtr hdc, int originX, int originY)
    {
        var info = new CURSORINFO();
        info.cbSize = Marshal.SizeOf<CURSORINFO>();

        if (!GetCursorInfo(ref info) || info.flags != CURSOR_SHOWING || info.hCursor == IntPtr.Zero)
            return CursorSnapshot.Hidden;

        int x = info.ptScreenPos.X - originX;
        int y = info.ptScreenPos.Y - originY;

        // Sahiplik bizde olsun diye kopyala; sistem imleci her an değişebilir.
        var icon = CopyIcon(info.hCursor);
        if (icon == IntPtr.Zero)
            return new CursorSnapshot(x, y, true);

        try
        {
            if (!GetIconInfo(icon, out var iconInfo))
                return new CursorSnapshot(x, y, true);

            try
            {
                int drawX = x - iconInfo.xHotspot;
                int drawY = y - iconInfo.yHotspot;

                // Animasyonlu imleçte geçersiz adım hataya yol açar → başa sar.
                if (!DrawIconEx(hdc, drawX, drawY, info.hCursor, 0, 0, _step, IntPtr.Zero, DI_NORMAL))
                {
                    _step = 0;
                    DrawIconEx(hdc, drawX, drawY, info.hCursor, 0, 0, _step, IntPtr.Zero, DI_NORMAL);
                }
                else
                {
                    _step++;
                }
            }
            finally
            {
                if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
                if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }

        return new CursorSnapshot(x, y, true);
    }

    public void Reset() => _step = 0;
}
