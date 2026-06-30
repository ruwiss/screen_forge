using System.IO;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// LZW image compression. Adapted from ScreenToGif (Nicke Manarin),
/// originally based on Jef Poskanzer's Java port.
/// </summary>
internal sealed class LzwEncoder
{
    private const int Eof = -1;
    private const int Bits = 12;
    private const int HSize = 5003;
    private const int MaxMaxCode = 1 << Bits;

    private readonly byte[] _pixAry;
    private readonly int _initCodeSize;
    private int _curPixel;
    private int _numBits;
    private readonly int _maxBits = Bits;
    private int _maxCode;
    private int[] htab = new int[HSize];
    private readonly int[] _codeTab = new int[HSize];
    private int _hSize = HSize;
    private int _freeEntry;
    private bool clear_flg;
    private int g_init_bits;
    private int ClearCode;
    private int EOFCode;
    private int cur_accum;
    private int cur_bits;
    private int _charCount;
    private readonly byte[] _accumulator = new byte[256];

    private static readonly int[] Masks =
    {
        0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F, 0x003F, 0x007F,
        0x00FF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF,
    };

    public LzwEncoder(int width, int height, byte[] pixels, int colorDepth)
    {
        _pixAry = pixels;
        _initCodeSize = Math.Max(2, colorDepth);
    }

    public void Encode(Stream os)
    {
        os.WriteByte(Convert.ToByte(_initCodeSize));
        _curPixel = 0;
        Compress(_initCodeSize + 1, os);
        os.WriteByte(0);
    }

    private void Add(byte c, Stream outs)
    {
        _accumulator[_charCount++] = c;
        if (_charCount >= 254) Flush(outs);
    }

    private void ClearTable(Stream outs)
    {
        ResetCodeTable(_hSize);
        _freeEntry = ClearCode + 2;
        clear_flg = true;
        Output(ClearCode, outs);
    }

    private void ResetCodeTable(int hsize)
    {
        for (int i = 0; i < hsize; ++i) htab[i] = -1;
    }

    private void Compress(int initBits, Stream outs)
    {
        g_init_bits = initBits;
        clear_flg = false;
        _numBits = g_init_bits;
        _maxCode = MaxCode(_numBits);
        ClearCode = 1 << (initBits - 1);
        EOFCode = ClearCode + 1;
        _freeEntry = ClearCode + 2;
        _charCount = 0;

        var ent = NextPixel();
        var hshift = 0;
        for (int fcode2 = _hSize; fcode2 < 65536; fcode2 *= 2) ++hshift;
        hshift = 8 - hshift;
        var hsizeReg = _hSize;
        ResetCodeTable(hsizeReg);
        Output(ClearCode, outs);

        int c;
        while ((c = NextPixel()) != Eof)
        {
            var fcode = (c << _maxBits) + ent;
            var i = (c << hshift) ^ ent;

            if (htab[i] == fcode) { ent = _codeTab[i]; continue; }

            if (htab[i] >= 0)
            {
                var disp = hsizeReg - i;
                if (i == 0) disp = 1;
                bool found = false;
                do
                {
                    if ((i -= disp) < 0) i += hsizeReg;
                    if (htab[i] == fcode) { ent = _codeTab[i]; found = true; break; }
                } while (htab[i] >= 0);
                if (found) continue;
            }

            Output(ent, outs);
            ent = c;
            if (_freeEntry < MaxMaxCode) { _codeTab[i] = _freeEntry++; htab[i] = fcode; }
            else ClearTable(outs);
        }

        Output(ent, outs);
        Output(EOFCode, outs);
    }

    private void Flush(Stream outs)
    {
        if (_charCount > 0)
        {
            outs.WriteByte(Convert.ToByte(_charCount));
            outs.Write(_accumulator, 0, _charCount);
            _charCount = 0;
        }
    }

    private int MaxCode(int numBits) => (1 << numBits) - 1;

    private int NextPixel()
    {
        if (_curPixel <= _pixAry.GetUpperBound(0))
            return _pixAry[_curPixel++] & 0xff;
        return Eof;
    }

    private void Output(int code, Stream outs)
    {
        cur_accum &= Masks[cur_bits];
        cur_accum = cur_bits > 0 ? cur_accum | (code << cur_bits) : code;
        cur_bits += _numBits;

        while (cur_bits >= 8)
        {
            Add((byte)(cur_accum & 0xff), outs);
            cur_accum >>= 8;
            cur_bits -= 8;
        }

        if (_freeEntry > _maxCode || clear_flg)
        {
            if (clear_flg) { _maxCode = MaxCode(_numBits = g_init_bits); clear_flg = false; }
            else { ++_numBits; _maxCode = _numBits == _maxBits ? MaxMaxCode : MaxCode(_numBits); }
        }

        if (code == EOFCode)
        {
            while (cur_bits > 0) { Add((byte)(cur_accum & 0xff), outs); cur_accum >>= 8; cur_bits -= 8; }
            Flush(outs);
        }
    }
}
