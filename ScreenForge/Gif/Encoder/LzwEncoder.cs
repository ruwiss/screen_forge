using System.IO;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// GIF LZW sıkıştırıcı. ScreenToGif (Nicke Manarin) üzerinden Jef Poskanzer'ın
/// Java uyarlamasına dayanır. Değişken başlangıç kod boyutunu destekler:
/// 64 renklik palet 6 bitlik kodla yazılır, 8 bit yerine → çıktı belirgin küçülür.
/// </summary>
internal sealed class LzwEncoder
{
    private const int Eof = -1;
    private const int MaxBits = 12;
    private const int HashSize = 5003;
    private const int MaxMaxCode = 1 << MaxBits;

    private static readonly int[] Masks =
    {
        0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F, 0x003F, 0x007F,
        0x00FF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF,
    };

    private readonly byte[] _pixels;
    private readonly int _initialCodeSize;
    private readonly int[] _hashTable = new int[HashSize];
    private readonly int[] _codeTable = new int[HashSize];
    private readonly byte[] _accumulator = new byte[256];

    private int _currentPixel;
    private int _numBits;
    private int _maxCode;
    private int _freeEntry;
    private bool _clearFlag;
    private int _initialBits;
    private int _clearCode;
    private int _eofCode;
    private int _bitAccumulator;
    private int _bitCount;
    private int _charCount;

    /// <param name="indexedPixels">Palet indeksleri (piksel başına 1 bayt).</param>
    /// <param name="colorDepth">Palet için gereken bit sayısı (1-8).</param>
    public LzwEncoder(byte[] indexedPixels, int colorDepth)
    {
        _pixels = indexedPixels;
        // GIF minimum LZW kod boyutu en az 2 olmalıdır.
        _initialCodeSize = Math.Clamp(colorDepth, 2, 8);
    }

    public void Encode(Stream output)
    {
        output.WriteByte((byte)_initialCodeSize);
        _currentPixel = 0;
        Compress(_initialCodeSize + 1, output);
        output.WriteByte(0); // blok sonlandırıcı
    }

    private void Compress(int initialBits, Stream output)
    {
        _initialBits = initialBits;
        _clearFlag = false;
        _numBits = _initialBits;
        _maxCode = MaxCode(_numBits);
        _clearCode = 1 << (initialBits - 1);
        _eofCode = _clearCode + 1;
        _freeEntry = _clearCode + 2;
        _charCount = 0;
        _bitAccumulator = 0;
        _bitCount = 0;

        int entry = NextPixel();
        if (entry == Eof)
        {
            Output(_clearCode, output);
            Output(_eofCode, output);
            return;
        }

        int hashShift = 0;
        for (int code = HashSize; code < 65536; code *= 2) hashShift++;
        hashShift = 8 - hashShift;

        ResetHashTable();
        Output(_clearCode, output);

        int c;
        while ((c = NextPixel()) != Eof)
        {
            int fcode = (c << MaxBits) + entry;
            int i = (c << hashShift) ^ entry;

            if (_hashTable[i] == fcode)
            {
                entry = _codeTable[i];
                continue;
            }

            if (_hashTable[i] >= 0)
            {
                int disp = i == 0 ? 1 : HashSize - i;
                bool found = false;
                do
                {
                    if ((i -= disp) < 0) i += HashSize;
                    if (_hashTable[i] == fcode) { entry = _codeTable[i]; found = true; break; }
                } while (_hashTable[i] >= 0);
                if (found) continue;
            }

            Output(entry, output);
            entry = c;

            if (_freeEntry < MaxMaxCode)
            {
                _codeTable[i] = _freeEntry++;
                _hashTable[i] = fcode;
            }
            else
            {
                ResetHashTable();
                _freeEntry = _clearCode + 2;
                _clearFlag = true;
                Output(_clearCode, output);
            }
        }

        Output(entry, output);
        Output(_eofCode, output);
    }

    private void ResetHashTable()
    {
        for (int i = 0; i < HashSize; i++) _hashTable[i] = -1;
    }

    private static int MaxCode(int numBits) => (1 << numBits) - 1;

    private int NextPixel() => _currentPixel < _pixels.Length ? _pixels[_currentPixel++] & 0xff : Eof;

    private void Output(int code, Stream output)
    {
        _bitAccumulator &= Masks[_bitCount];
        _bitAccumulator = _bitCount > 0 ? _bitAccumulator | (code << _bitCount) : code;
        _bitCount += _numBits;

        while (_bitCount >= 8)
        {
            Add((byte)(_bitAccumulator & 0xff), output);
            _bitAccumulator >>= 8;
            _bitCount -= 8;
        }

        if (_freeEntry > _maxCode || _clearFlag)
        {
            if (_clearFlag)
            {
                _numBits = _initialBits;
                _maxCode = MaxCode(_numBits);
                _clearFlag = false;
            }
            else
            {
                _numBits++;
                _maxCode = _numBits == MaxBits ? MaxMaxCode : MaxCode(_numBits);
            }
        }

        if (code != _eofCode)
            return;

        while (_bitCount > 0)
        {
            Add((byte)(_bitAccumulator & 0xff), output);
            _bitAccumulator >>= 8;
            _bitCount -= 8;
        }
        Flush(output);
    }

    private void Add(byte value, Stream output)
    {
        _accumulator[_charCount++] = value;
        if (_charCount >= 254) Flush(output);
    }

    private void Flush(Stream output)
    {
        if (_charCount <= 0)
            return;

        output.WriteByte((byte)_charCount);
        output.Write(_accumulator, 0, _charCount);
        _charCount = 0;
    }
}
