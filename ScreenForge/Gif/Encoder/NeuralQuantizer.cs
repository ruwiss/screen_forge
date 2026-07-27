using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

// NeuQuant Neural-Net Quantization Algorithm
// ScreenToGif üzerinden uyarlandı — Anthony Dekker, Kevin Weiner, gOODiDEA.NET, Simon Bridewell, Nicke Manarin
// GNU GPL v3 — http://www.gnu.org/licenses/gpl.html

/// <summary>
/// Kendini düzenleyen sinir ağı ile renk kuantalaması. Octree'ye göre yavaş ama
/// fotoğrafik içerikte belirgin daha iyi palet üretir.
/// </summary>
internal sealed class NeuralQuantizer : Quantizer
{
    private const int NetworkBiasShift = 4;
    private const int NumberOfLearningCycles = 100;

    private const int Prime1 = 499;
    private const int Prime2 = 491;
    private const int Prime3 = 487;
    private const int Prime4 = 503;

    private const int AlphaBiasShift = 10;
    private const int InitialAlpha = 1 << AlphaBiasShift;
    private const int IntBiasShift = 16;
    private const int IntBias = 1 << IntBiasShift;
    private const int GammaShift = 10;
    private const int BetaShift = 10;
    private const int ClosestNeuronFrequencyIncrement = IntBias >> BetaShift;
    private const int ClosestNeuronBiasDecrement = IntBias << (GammaShift - BetaShift);

    private const int NeighbourhoodSizeBiasShift = 6;
    private const int NeighbourhoodSizeBias = 1 << NeighbourhoodSizeBiasShift;
    private const int UnbiasedNeighbourhoodSizeDecrement = 30;

    private const int RadiusBiasShift = 8;
    private const int RadiusBias = 1 << RadiusBiasShift;
    private const int AlphaRadiusBias = 1 << (AlphaBiasShift + RadiusBiasShift);

    private readonly int _networkSize;
    private int _samplingFactor;

    private int[][] _network = Array.Empty<int[]>();
    private int[] _biases = Array.Empty<int>();
    private int[] _frequencies = Array.Empty<int>();
    private int[] _neighbourhoodAlphas = Array.Empty<int>();
    private int _initialUnbiasedNeighbourhoodSize;

    /// <param name="samplingFactor">1-20. 1 = en yüksek kalite (yavaş), 20 = en hızlı.</param>
    /// <param name="maximumColors">Üretilecek renk sayısı (2-256).</param>
    public NeuralQuantizer(int samplingFactor, int maximumColors = 256)
    {
        _samplingFactor = Math.Clamp(samplingFactor, 1, 20);
        _networkSize = Math.Clamp(maximumColors, 2, 256);
        MaxColors = _networkSize;
    }

    internal override void FirstPass(byte[] bgra)
    {
        int size = Math.Clamp(Math.Min(MaxColors, _networkSize), 2, 256);

        _network = new int[size][];
        _biases = new int[size];
        _frequencies = new int[size];

        int initialNeighbourhood = Math.Max(size >> 3, 1);
        _neighbourhoodAlphas = new int[initialNeighbourhood];
        _initialUnbiasedNeighbourhoodSize = initialNeighbourhood * NeighbourhoodSizeBias;

        for (int n = 0; n < size; n++)
        {
            _network[n] = new int[4];
            _network[n][0] = _network[n][1] = _network[n][2] = (n << (NetworkBiasShift + 8)) / size;
            _frequencies[n] = IntBias / size;
        }

        Learn(bgra);
        UnbiasNetwork();
    }

    internal override List<WpfColor> BuildPalette()
    {
        var colors = new List<WpfColor>(_network.Length);
        foreach (var neuron in _network)
            colors.Add(WpfColor.FromRgb((byte)neuron[2], (byte)neuron[1], (byte)neuron[0]));

        return colors;
    }

    // ─── Öğrenme ──────────────────────────────────────────────────────────────

    private void Learn(byte[] pixels)
    {
        int byteCount = pixels.Length;
        if (byteCount < BytesPerPixel)
            return;

        if (byteCount < Prime4 * BytesPerPixel)
            _samplingFactor = 1;

        int alphaDecrement = 30 + (_samplingFactor - 1) / 4;
        int pixelsToExamine = Math.Max(1, byteCount / (BytesPerPixel * _samplingFactor));
        int alphaUpdateFrequency = Math.Max(1, pixelsToExamine / NumberOfLearningCycles);
        int alpha = InitialAlpha;
        int unbiasedSize = _initialUnbiasedNeighbourhoodSize;
        int neighbourhoodSize = Math.Max(1, unbiasedSize >> NeighbourhoodSizeBiasShift);

        SetNeighbourhoodAlphas(neighbourhoodSize, alpha);

        int step = GetPixelIndexIncrement(byteCount);
        int pixelIndex = 0;

        for (int examined = 0; examined < pixelsToExamine; examined++)
        {
            if (pixelIndex + 3 < byteCount && pixels[pixelIndex + 3] > 0)
            {
                int b = (pixels[pixelIndex] & 0xff) << NetworkBiasShift;
                int g = (pixels[pixelIndex + 1] & 0xff) << NetworkBiasShift;
                int r = (pixels[pixelIndex + 2] & 0xff) << NetworkBiasShift;

                int best = FindBestNeuron(b, g, r);
                MoveNeuron(alpha, best, b, g, r);
                if (neighbourhoodSize != 0)
                    MoveNeighbouringNeurons(neighbourhoodSize, best, b, g, r);
            }

            pixelIndex += step;
            if (pixelIndex >= byteCount) pixelIndex -= byteCount;

            if (examined % alphaUpdateFrequency != 0)
                continue;

            alpha -= alpha / alphaDecrement;
            unbiasedSize -= unbiasedSize / UnbiasedNeighbourhoodSizeDecrement;
            neighbourhoodSize = unbiasedSize >> NeighbourhoodSizeBiasShift;
            if (neighbourhoodSize <= 1) neighbourhoodSize = 0;
            SetNeighbourhoodAlphas(neighbourhoodSize, alpha);
        }
    }

    private void SetNeighbourhoodAlphas(int size, int alpha)
    {
        if (size <= 0)
            return;

        size = Math.Min(size, _neighbourhoodAlphas.Length);
        int squared = size * size;
        for (int i = 0; i < size; i++)
            _neighbourhoodAlphas[i] = alpha * ((squared - i * i) * RadiusBias / squared);
    }

    private static int GetPixelIndexIncrement(int byteCount)
    {
        if (byteCount < Prime4 * BytesPerPixel) return BytesPerPixel;
        if (byteCount % Prime1 != 0) return Prime1 * BytesPerPixel;
        if (byteCount % Prime2 != 0) return Prime2 * BytesPerPixel;
        if (byteCount % Prime3 != 0) return Prime3 * BytesPerPixel;
        return Prime4 * BytesPerPixel;
    }

    private int FindBestNeuron(int blue, int green, int red)
    {
        int bestDistance = int.MaxValue;
        int bestBiasDistance = int.MaxValue;
        int closestIndex = 0;
        int bestBiasIndex = 0;

        for (int n = 0; n < _network.Length; n++)
        {
            var neuron = _network[n];

            int distance = Math.Abs(neuron[0] - blue)
                         + Math.Abs(neuron[1] - green)
                         + Math.Abs(neuron[2] - red);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                closestIndex = n;
            }

            int biasDistance = distance - (_biases[n] >> (IntBiasShift - NetworkBiasShift));
            if (biasDistance < bestBiasDistance)
            {
                bestBiasDistance = biasDistance;
                bestBiasIndex = n;
            }

            int frequencyDelta = _frequencies[n] >> BetaShift;
            _frequencies[n] -= frequencyDelta;
            _biases[n] += frequencyDelta << GammaShift;
        }

        _frequencies[closestIndex] += ClosestNeuronFrequencyIncrement;
        _biases[closestIndex] -= ClosestNeuronBiasDecrement;
        return bestBiasIndex;
    }

    private void MoveNeuron(int alpha, int index, int b, int g, int r)
    {
        var neuron = _network[index];
        neuron[0] -= alpha * (neuron[0] - b) / InitialAlpha;
        neuron[1] -= alpha * (neuron[1] - g) / InitialAlpha;
        neuron[2] -= alpha * (neuron[2] - r) / InitialAlpha;
    }

    private void MoveNeighbouringNeurons(int size, int index, int b, int g, int r)
    {
        int low = Math.Max(index - size, -1);
        int high = Math.Min(index + size, _network.Length);

        int highIndex = index + 1;
        int lowIndex = index - 1;
        int alphaIndex = 1;

        while ((highIndex < high || lowIndex > low) && alphaIndex < _neighbourhoodAlphas.Length)
        {
            int alpha = _neighbourhoodAlphas[alphaIndex++];
            if (highIndex < high) MoveNeighbour(highIndex++, alpha, b, g, r);
            if (lowIndex > low) MoveNeighbour(lowIndex--, alpha, b, g, r);
        }
    }

    private void MoveNeighbour(int index, int alpha, int b, int g, int r)
    {
        var neuron = _network[index];
        neuron[0] -= alpha * (neuron[0] - b) / AlphaRadiusBias;
        neuron[1] -= alpha * (neuron[1] - g) / AlphaRadiusBias;
        neuron[2] -= alpha * (neuron[2] - r) / AlphaRadiusBias;
    }

    private void UnbiasNetwork()
    {
        foreach (var neuron in _network)
        {
            neuron[0] = Math.Clamp(neuron[0] >> NetworkBiasShift, 0, 255);
            neuron[1] = Math.Clamp(neuron[1] >> NetworkBiasShift, 0, 255);
            neuron[2] = Math.Clamp(neuron[2] >> NetworkBiasShift, 0, 255);
        }
    }
}
