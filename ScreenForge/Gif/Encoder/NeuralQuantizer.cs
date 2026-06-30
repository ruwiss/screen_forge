using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

// NeuQuant Neural-Net Quantization Algorithm
// Ported from ScreenToGif — Anthony Dekker, Kevin Weiner, gOODiDEA.NET, Simon Bridewell, Nicke Manarin
// GNU GPL v3 — http://www.gnu.org/licenses/gpl.html

internal sealed class NeuralQuantizer : Quantizer
{
    private readonly int _networkSize;
    private int _maximumNeuronIndex;

    private const int NetworkBiasShift       = 4;
    private const int NumberOfLearningCycles = 100;

    private const int Prime1 = 499;
    private const int Prime2 = 491;
    private const int Prime3 = 487;
    private const int Prime4 = 503;

    private int[] _biases          = Array.Empty<int>();
    private int[] _frequencies     = Array.Empty<int>();
    private int[] _neighbourhoodAlphas = Array.Empty<int>();

    private const int AlphaBiasShift    = 10;
    private const int InitialAlpha      = 1 << AlphaBiasShift;
    private const int IntBiasShift      = 16;
    private const int IntBias           = 1 << IntBiasShift;
    private const int GammaShift        = 10;
    private const int BetaShift         = 10;
    private const int ClosestNeuronFrequencyIncrement = IntBias >> BetaShift;
    private const int ClosestNeuronBiasDecrement      = IntBias << (GammaShift - BetaShift);

    private int _initialNeighbourhoodSize;
    private const int NeighbourhoodSizeBiasShift      = 6;
    private const int NeighbourhoodSizeBias            = 1 << NeighbourhoodSizeBiasShift;
    private int _initialUnbiasedNeighbourhoodSize;
    private const int UnbiasedNeighbourhoodSizeDecrement = 30;

    private const int RadiusBiasShift       = 8;
    private const int RadiusBias            = 1 << RadiusBiasShift;
    private const int AlphaRadiusBiasShift  = AlphaBiasShift + RadiusBiasShift;
    private const int AlphaRadiusBias       = 1 << AlphaRadiusBiasShift;

    private int _pixelBytesCount;
    private int _samplingFactor;

    private int[][] _network   = Array.Empty<int[]>();
    private int[] _indexOfGreen = Array.Empty<int>();

    /// <param name="samplingFactor">1-20. 1=en yüksek kalite (yavaş), 10=varsayılan, 20=en hızlı.</param>
    /// <param name="maximumColors">Maksimum renk sayısı (<=256).</param>
    public NeuralQuantizer(int samplingFactor, int maximumColors = 256) : base(false)
    {
        _samplingFactor = samplingFactor;
        _networkSize    = maximumColors;
    }

    internal override void FirstPass(byte[] pixels)
    {
        MaxColorsWithTransparency = TransparentColor.HasValue ? _networkSize - 1 : _networkSize;
        _maximumNeuronIndex       = MaxColorsWithTransparency - 1;
        _network      = new int[MaxColorsWithTransparency][];
        _indexOfGreen = new int[256];
        _biases       = new int[MaxColorsWithTransparency];
        _frequencies  = new int[MaxColorsWithTransparency];
        _initialNeighbourhoodSize        = Math.Max(MaxColorsWithTransparency >> 3, 1);
        _neighbourhoodAlphas             = new int[_initialNeighbourhoodSize];
        _initialUnbiasedNeighbourhoodSize = _initialNeighbourhoodSize * NeighbourhoodSizeBias;

        for (var n = 0; n < MaxColorsWithTransparency; n++)
        {
            _network[n]    = new int[4];
            _network[n][0] = _network[n][1] = _network[n][2] =
                (n << (NetworkBiasShift + 8)) / MaxColorsWithTransparency;
            _frequencies[n] = IntBias / MaxColorsWithTransparency;
            _biases[n]      = 0;
        }

        Learn(pixels);
        UnbiasNetwork();
        BuildIndex();
    }

    internal override List<WpfColor> BuildPalette()
    {
        var map   = new byte[3 * MaxColorsWithTransparency];
        var index = new int[MaxColorsWithTransparency];
        for (var i = 0; i < MaxColorsWithTransparency; i++)
            index[_network[i][3]] = i;

        var colors = new List<WpfColor>(MaxColorsWithTransparency + 1);
        var k = 0;
        for (var i = 0; i < MaxColorsWithTransparency; i++)
        {
            var j = index[i];
            map[k++] = (byte)_network[j][0]; // B
            map[k++] = (byte)_network[j][1]; // G
            map[k++] = (byte)_network[j][2]; // R
            colors.Add(new WpfColor { A = 255, B = map[k - 3], G = map[k - 2], R = map[k - 1] });
        }

        if (TransparentColor.HasValue)
            colors.Add(TransparentColor.Value);

        return colors;
    }

    protected override byte QuantizePixel(WpfColor pixel) => MapColor(pixel.B, pixel.G, pixel.R);

    // ─── Learning ────────────────────────────────────────────────────────────────

    private void Learn(byte[] pixels)
    {
        _pixelBytesCount = pixels.Length;

        if (_pixelBytesCount < Prime4 * 4)
            _samplingFactor = 1;

        var alphaDecrement    = 30 + (_samplingFactor - 1) / 4;
        var pixelIndex        = 0;
        var pixelsToExamine   = _pixelBytesCount / (4 * _samplingFactor);
        var alphaUpdateFreq   = Math.Max(1, pixelsToExamine / NumberOfLearningCycles);
        var alpha             = InitialAlpha;
        var unbiasedSize      = _initialUnbiasedNeighbourhoodSize;
        var neighbourhoodSize = unbiasedSize >> NeighbourhoodSizeBiasShift;
        if (neighbourhoodSize < 1) neighbourhoodSize = 1;

        SetNeighbourhoodAlphas(_neighbourhoodAlphas, neighbourhoodSize, alpha, RadiusBias);
        var step = GetPixelIndexIncrement(_pixelBytesCount);

        for (var examined = 0; examined < pixelsToExamine; examined++)
        {
            if (pixels[pixelIndex + 3] > 0)
            {
                var b = (pixels[pixelIndex + 0] & 0xff) << NetworkBiasShift;
                var g = (pixels[pixelIndex + 1] & 0xff) << NetworkBiasShift;
                var r = (pixels[pixelIndex + 2] & 0xff) << NetworkBiasShift;

                var best = FindClosestAndReturnBestNeuron(b, g, r);
                MoveNeuron(alpha, best, b, g, r);
                if (neighbourhoodSize != 0)
                    MoveNeighbouringNeurons(neighbourhoodSize, best, b, g, r);
            }

            pixelIndex += step;
            if (pixelIndex >= _pixelBytesCount) pixelIndex -= _pixelBytesCount;

            if (examined % alphaUpdateFreq == 0)
            {
                alpha       -= alpha       / alphaDecrement;
                unbiasedSize -= unbiasedSize / UnbiasedNeighbourhoodSizeDecrement;
                neighbourhoodSize = unbiasedSize >> NeighbourhoodSizeBiasShift;
                if (neighbourhoodSize <= 1) neighbourhoodSize = 0;
                SetNeighbourhoodAlphas(_neighbourhoodAlphas, neighbourhoodSize, alpha, RadiusBias);
            }
        }
    }

    private static void SetNeighbourhoodAlphas(int[] alphas, int size, int alpha, int radiusBias)
    {
        var sq = size * size;
        for (var i = 0; i < size; i++)
            alphas[i] = alpha * ((sq - i * i) * radiusBias / sq);
    }

    private static int GetPixelIndexIncrement(int byteCount)
    {
        if (byteCount < Prime4 * 4)          return 4;
        if (byteCount % Prime1 != 0)          return Prime1 * 4;
        if (byteCount % Prime2 != 0)          return Prime2 * 4;
        if (byteCount % Prime3 != 0)          return Prime3 * 4;
        return Prime4 * 4;
    }

    private int FindClosestAndReturnBestNeuron(int blue, int green, int red)
    {
        var bestDist     = ~(1 << 31);
        var bestBiasDist = bestDist;
        var closestIdx   = -1;
        var bestBiasIdx  = -1;

        for (var n = 0; n < MaxColorsWithTransparency; n++)
        {
            var dist = _network[n][0] - blue;
            if (dist < 0) dist = -dist;
            var d2 = _network[n][1] - green; if (d2 < 0) d2 = -d2; dist += d2;
            d2 = _network[n][2] - red;       if (d2 < 0) d2 = -d2; dist += d2;

            if (dist < bestDist) { bestDist = dist; closestIdx = n; }

            var biasDist = dist - (_biases[n] >> (IntBiasShift - NetworkBiasShift));
            if (biasDist < bestBiasDist) { bestBiasDist = biasDist; bestBiasIdx = n; }

            var bf = _frequencies[n] >> BetaShift;
            _frequencies[n] -= bf;
            _biases[n]       += bf << GammaShift;
        }

        _frequencies[closestIdx] += ClosestNeuronFrequencyIncrement;
        _biases[closestIdx]      -= ClosestNeuronBiasDecrement;
        return bestBiasIdx;
    }

    private void MoveNeuron(int alpha, int idx, int b, int g, int r)
    {
        _network[idx][0] -= (alpha * (_network[idx][0] - b)) / InitialAlpha;
        _network[idx][1] -= (alpha * (_network[idx][1] - g)) / InitialAlpha;
        _network[idx][2] -= (alpha * (_network[idx][2] - r)) / InitialAlpha;
    }

    private void MoveNeighbouringNeurons(int size, int idx, int b, int g, int r)
    {
        var lo = idx - size; if (lo < -1) lo = -1;
        var hi = idx + size; if (hi > _network.Length) hi = _network.Length;

        var hiIdx = idx + 1;
        var loIdx = idx - 1;
        var ai    = 1;

        while (hiIdx < hi || loIdx > lo)
        {
            var na = _neighbourhoodAlphas[ai++];
            if (hiIdx < hi) MoveNeighbour(hiIdx++, na, AlphaRadiusBias, b, g, r);
            if (loIdx > lo) MoveNeighbour(loIdx--, na, AlphaRadiusBias, b, g, r);
        }
    }

    private void MoveNeighbour(int idx, int alpha, int arb, int b, int g, int r)
    {
        _network[idx][0] -= (alpha * (_network[idx][0] - b)) / arb;
        _network[idx][1] -= (alpha * (_network[idx][1] - g)) / arb;
        _network[idx][2] -= (alpha * (_network[idx][2] - r)) / arb;
    }

    private void UnbiasNetwork()
    {
        for (var n = 0; n < MaxColorsWithTransparency; n++)
        {
            _network[n][0] >>= NetworkBiasShift;
            _network[n][1] >>= NetworkBiasShift;
            _network[n][2] >>= NetworkBiasShift;
            _network[n][3]   = n;
        }
    }

    private void BuildIndex()
    {
        var prevGreen  = 0;
        var startGreen = 0;

        for (var i = 0; i < MaxColorsWithTransparency; i++)
        {
            var cur = _network[i];
            var li  = IndexOfLeastGreen(i);
            var lv  = _network[li][1];

            if (i != li) SwapNeurons(cur, _network[li]);

            if (lv != prevGreen)
            {
                _indexOfGreen[prevGreen] = (startGreen + i) >> 1;
                for (var g = prevGreen + 1; g < lv; g++) _indexOfGreen[g] = i;
                prevGreen  = lv;
                startGreen = i;
            }
        }

        _indexOfGreen[prevGreen] = (startGreen + _maximumNeuronIndex) >> 1;
        for (var g = prevGreen + 1; g < 256; g++) _indexOfGreen[g] = _maximumNeuronIndex;
    }

    private int IndexOfLeastGreen(int start)
    {
        var bestIdx = start;
        var bestG   = _network[start][1];
        for (var j = start + 1; j < MaxColorsWithTransparency; j++)
            if (_network[j][1] < bestG) { bestIdx = j; bestG = _network[j][1]; }
        return bestIdx;
    }

    private static void SwapNeurons(int[] a, int[] b)
    {
        for (var i = 0; i < a.Length; i++) { var t = a[i]; a[i] = b[i]; b[i] = t; }
    }

    // ─── Color lookup ─────────────────────────────────────────────────────────────

    internal byte MapColor(int blue, int green, int red)
    {
        var best    = -1;
        var bestDist = 1000;
        var hi      = _indexOfGreen[green];
        var lo      = hi - 1;

        while (hi < MaxColorsWithTransparency || lo >= 0)
        {
            if (hi < MaxColorsWithTransparency)
            {
                var n = _network[hi];
                var d = n[1] - green;
                if (d >= bestDist) { hi = MaxColorsWithTransparency; }
                else
                {
                    hi++;
                    if (d < 0) d = -d;
                    var d2 = n[0] - blue; if (d2 < 0) d2 = -d2; d += d2;
                    if (d < bestDist) { d2 = n[2] - red; if (d2 < 0) d2 = -d2; d += d2; if (d < bestDist) { bestDist = d; best = n[3]; } }
                }
            }

            if (lo >= 0)
            {
                var n = _network[lo];
                var d = green - n[1];
                if (d >= bestDist) { lo = -1; }
                else
                {
                    lo--;
                    if (d < 0) d = -d;
                    var d2 = n[0] - blue; if (d2 < 0) d2 = -d2; d += d2;
                    if (d < bestDist) { d2 = n[2] - red; if (d2 < 0) d2 = -d2; d += d2; if (d < bestDist) { bestDist = d; best = n[3]; } }
                }
            }
        }

        return (byte)Math.Min(best, MaxColorsWithTransparency);
    }
}
