using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Gif.Encoder;

/// <summary>
/// Octree renk kuantalaması — hızlı, düşük bellekli. ScreenToGif uyarlaması.
/// </summary>
internal sealed class OctreeQuantizer : Quantizer
{
    private const int ColorBits = 8;

    private readonly Octree _octree = new(ColorBits);

    internal override void FirstPass(byte[] bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += BytesPerPixel)
        {
            if (bgra[i + 3] == 0)
                continue;

            _octree.AddColor(bgra[i + 2], bgra[i + 1], bgra[i]);
        }
    }

    internal override List<WpfColor> BuildPalette()
        => _octree.Palletize(Math.Clamp(MaxColors, 2, 256));

    private sealed class Octree
    {
        private static readonly int[] Mask = { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 };

        private readonly OctreeNode _root;
        private readonly int _maxColorBits;
        private readonly OctreeNode?[] _reducibleNodes = new OctreeNode?[ColorBits + 1];

        private OctreeNode? _previousNode;
        private int _previousColor = -1;
        private int _leaves;

        public Octree(int maxColorBits)
        {
            _maxColorBits = maxColorBits;
            _root = new OctreeNode(0, _maxColorBits, this);
        }

        public void AddColor(byte r, byte g, byte b)
        {
            int packed = (r << 16) | (g << 8) | b;
            if (packed == _previousColor && _previousNode != null)
            {
                _previousNode.Increment(r, g, b);
                return;
            }

            _previousColor = packed;
            _root.AddColor(r, g, b, _maxColorBits, 0, this);
        }

        public List<WpfColor> Palletize(int colorCount)
        {
            while (_leaves > colorCount) Reduce();

            var palette = new List<WpfColor>(_leaves);
            int index = 0;
            _root.ConstructPalette(palette, ref index);
            return palette;
        }

        private void Reduce()
        {
            int level;
            for (level = _maxColorBits - 1; level > 0 && _reducibleNodes[level] == null; level--) { }

            var node = _reducibleNodes[level];
            if (node == null)
                return;

            _reducibleNodes[level] = node.NextReducible;
            _leaves -= node.Reduce();
            _previousNode = null;
            _previousColor = -1;
        }

        private void TrackPrevious(OctreeNode node) => _previousNode = node;

        private sealed class OctreeNode
        {
            private readonly OctreeNode?[]? _children;
            private bool _leaf;
            private int _pixelCount, _red, _green, _blue, _paletteIndex;

            public OctreeNode? NextReducible { get; }

            public OctreeNode(int level, int colorBits, Octree octree)
            {
                _leaf = level == colorBits;

                if (_leaf)
                {
                    octree._leaves++;
                    return;
                }

                NextReducible = octree._reducibleNodes[level];
                octree._reducibleNodes[level] = this;
                _children = new OctreeNode?[8];
            }

            public void AddColor(byte r, byte g, byte b, int colorBits, int level, Octree octree)
            {
                if (_leaf)
                {
                    Increment(r, g, b);
                    octree.TrackPrevious(this);
                    return;
                }

                int index = ChildIndex(r, g, b, level);
                var child = _children![index] ??= new OctreeNode(level + 1, colorBits, octree);
                child.AddColor(r, g, b, colorBits, level + 1, octree);
            }

            public int Reduce()
            {
                _red = _green = _blue = 0;
                int children = 0;

                for (int i = 0; i < 8; i++)
                {
                    var child = _children![i];
                    if (child == null)
                        continue;

                    _red += child._red;
                    _green += child._green;
                    _blue += child._blue;
                    _pixelCount += child._pixelCount;
                    children++;
                    _children[i] = null;
                }

                _leaf = true;
                return children - 1;
            }

            public void ConstructPalette(List<WpfColor> palette, ref int paletteIndex)
            {
                if (_leaf)
                {
                    _paletteIndex = paletteIndex++;
                    int count = Math.Max(1, _pixelCount);
                    palette.Add(WpfColor.FromRgb((byte)(_red / count), (byte)(_green / count), (byte)(_blue / count)));
                    return;
                }

                for (int i = 0; i < 8; i++)
                    _children![i]?.ConstructPalette(palette, ref paletteIndex);
            }

            public void Increment(byte r, byte g, byte b)
            {
                _pixelCount++;
                _red += r;
                _green += g;
                _blue += b;
            }

            private static int ChildIndex(byte r, byte g, byte b, int level)
            {
                int shift = 7 - level;
                return ((r & Mask[level]) >> (shift - 2))
                     | ((g & Mask[level]) >> (shift - 1))
                     | ((b & Mask[level]) >> shift);
            }
        }
    }
}
