using System.Collections;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace ScreenForge.Gif.Encoder;

internal sealed class OctreeQuantizer : Quantizer
{
    private readonly Octree _octree;

    public OctreeQuantizer(int maxColorBits = 8) : base(false)
    {
        if (maxColorBits is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(maxColorBits));
        _octree = new Octree(maxColorBits);
    }

    protected override void InitialQuantizePixel(WpfColor pixel)
    {
        if (pixel.A == 0) return;
        _octree.AddColor(pixel);
    }

    protected override byte QuantizePixel(WpfColor pixel) => (byte)_octree.GetPaletteIndex(pixel);

    internal override List<WpfColor> BuildPalette()
    {
        MaxColorsWithTransparency = TransparentColor.HasValue ? MaxColors - 1 : MaxColors;
        var palette = _octree.Palletize(MaxColorsWithTransparency);
        if (TransparentColor.HasValue)
            palette.Add(WpfColor.FromArgb(0, TransparentColor.Value.R, TransparentColor.Value.G, TransparentColor.Value.B));
        return palette.Cast<WpfColor>().ToList();
    }

    private sealed class Octree
    {
        private static readonly int[] Mask = { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 };
        private readonly OctreeNode _root;
        private OctreeNode[] ReducibleNodes { get; }
        private readonly int _maxColorBits;
        private OctreeNode? _previousNode;
        private WpfColor _previousColor;
        private int Leaves { get; set; }

        public Octree(int maxColorBits)
        {
            _maxColorBits = maxColorBits;
            Leaves = 0;
            ReducibleNodes = new OctreeNode[9];
            _root = new OctreeNode(0, _maxColorBits, this);
            _previousColor = WpfColors.Transparent;
            _previousNode = null;
        }

        public void AddColor(WpfColor pixel)
        {
            if (_previousColor == pixel)
            {
                if (_previousNode == null) { _previousColor = pixel; _root.AddColor(pixel, _maxColorBits, 0, this); }
                else _previousNode.Increment(pixel);
            }
            else { _previousColor = pixel; _root.AddColor(pixel, _maxColorBits, 0, this); }
        }

        private void Reduce()
        {
            int index;
            for (index = _maxColorBits - 1; index > 0 && ReducibleNodes[index] == null; index--) ;
            var node = ReducibleNodes[index];
            ReducibleNodes[index] = node!.NextReducible!;
            Leaves -= node.Reduce();
            _previousNode = null;
        }

        public void TrackPrevious(OctreeNode node) => _previousNode = node;

        public ArrayList Palletize(int colorCount)
        {
            while (Leaves > colorCount) Reduce();
            var palette = new ArrayList(Leaves);
            var paletteIndex = 0;
            _root.ConstructPalette(palette, ref paletteIndex);
            return palette;
        }

        public int GetPaletteIndex(WpfColor pixel) => _root.GetPaletteIndex(pixel, 0);

        internal sealed class OctreeNode
        {
            private bool _leaf;
            private int _pixelCount, _red, _green, _blue;
            private int _paletteIndex;
            public OctreeNode? NextReducible { get; private set; }
            private OctreeNode?[]? Children { get; }

            public OctreeNode(int level, int colorBits, Octree octree)
            {
                _leaf = level == colorBits;
                _red = _green = _blue = _pixelCount = 0;
                if (_leaf) { octree.Leaves++; NextReducible = null; Children = null; }
                else { NextReducible = octree.ReducibleNodes[level]; octree.ReducibleNodes[level] = this; Children = new OctreeNode[8]; }
            }

            public void AddColor(WpfColor pixel, int colorBits, int level, Octree octree)
            {
                if (_leaf) { Increment(pixel); octree.TrackPrevious(this); }
                else
                {
                    var shift = 7 - level;
                    var index = ((pixel.R & Mask[level]) >> (shift - 2)) |
                                ((pixel.G & Mask[level]) >> (shift - 1)) |
                                ((pixel.B & Mask[level]) >> shift);
                    var child = Children![index];
                    if (child == null) { child = new OctreeNode(level + 1, colorBits, octree); Children[index] = child; }
                    child.AddColor(pixel, colorBits, level + 1, octree);
                }
            }

            public int Reduce()
            {
                _red = _green = _blue = 0;
                var children = 0;
                for (var i = 0; i < 8; i++)
                {
                    if (Children![i] == null) continue;
                    _red += Children[i]!._red; _green += Children[i]!._green; _blue += Children[i]!._blue;
                    _pixelCount += Children[i]!._pixelCount; ++children; Children[i] = null;
                }
                _leaf = true;
                return children - 1;
            }

            public void ConstructPalette(IList palette, ref int paletteIndex)
            {
                if (_leaf) { _paletteIndex = paletteIndex++; palette.Add(WpfColor.FromRgb((byte)(_red / _pixelCount), (byte)(_green / _pixelCount), (byte)(_blue / _pixelCount))); }
                else { for (var i = 0; i < 8; i++) Children![i]?.ConstructPalette(palette, ref paletteIndex); }
            }

            public int GetPaletteIndex(WpfColor pixel, int level)
            {
                if (_leaf) return _paletteIndex;
                var shift = 7 - level;
                var index = ((pixel.R & Mask[level]) >> (shift - 2)) |
                            ((pixel.G & Mask[level]) >> (shift - 1)) |
                            ((pixel.B & Mask[level]) >> shift);
                return Children![index] != null
                    ? Children[index]!.GetPaletteIndex(pixel, level + 1)
                    : throw new Exception("Octree GetPaletteIndex: unexpected null child");
            }

            public void Increment(WpfColor pixel) { _pixelCount++; _red += pixel.R; _green += pixel.G; _blue += pixel.B; }
        }
    }
}
