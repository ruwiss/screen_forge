namespace ScreenForge.Windows;

internal static class WhiteCrossCursor
{
    private static System.Windows.Input.Cursor? _cursor;

    public static System.Windows.Input.Cursor Instance => _cursor ??= Create();

    private static System.Windows.Input.Cursor Create()
    {
        try
        {
            const int size = 32;
            const int hot = 15;
            byte[] pixels = new byte[size * size * 4];

            void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
            {
                if ((uint)x >= size || (uint)y >= size) return;
                int i = (y * size + x) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = a;
            }

            void DrawPlus(int cx, int cy, int len, int thickness, byte r, byte g, byte b, byte a)
            {
                int half = thickness / 2;
                for (int d = -half; d <= half; d++)
                {
                    for (int x = cx - len; x <= cx + len; x++) SetPixel(x, cy + d, r, g, b, a);
                    for (int y = cy - len; y <= cy + len; y++) SetPixel(cx + d, y, r, g, b, a);
                }
            }

            DrawPlus(hot, hot, 11, 3, 0x00, 0x00, 0x00, 0xC8);
            DrawPlus(hot, hot, 10, 1, 0xFF, 0xFF, 0xFF, 0xFF);

            using var ms = new System.IO.MemoryStream();
            using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                const int andStride = 4;
                uint imageSize = 40 + (uint)(size * size * 4) + (uint)(andStride * size);
                bw.Write((ushort)0);
                bw.Write((ushort)2);
                bw.Write((ushort)1);
                bw.Write((byte)size);
                bw.Write((byte)size);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)hot);
                bw.Write((ushort)hot);
                bw.Write(imageSize);
                bw.Write((uint)22);
                bw.Write((uint)40);
                bw.Write(size);
                bw.Write(size * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)0);
                bw.Write((uint)(size * size * 4));
                bw.Write(0);
                bw.Write(0);
                bw.Write((uint)0);
                bw.Write((uint)0);
                for (int y = size - 1; y >= 0; y--)
                    bw.Write(pixels, y * size * 4, size * 4);
                Span<byte> mask = stackalloc byte[andStride];
                for (int y = 0; y < size; y++) bw.Write(mask);
            }

            return new System.Windows.Input.Cursor(new System.IO.MemoryStream(ms.ToArray()));
        }
        catch
        {
            return System.Windows.Input.Cursors.Cross;
        }
    }
}
