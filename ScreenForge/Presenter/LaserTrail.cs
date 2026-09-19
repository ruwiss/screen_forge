using SkiaSharp;

namespace ScreenForge.Presenter;

public sealed class LaserTrail
{
    public const float MaxLength = 1800f;

    private readonly List<SKPoint> _points = [];
    private float _length;

    public IReadOnlyList<SKPoint> Points => _points;
    public bool IsEmpty => _points.Count == 0;
    public float Length => _length;
    public long ReleasedAt { get; private set; }

    public void Add(SKPoint p)
    {
        if (_points.Count > 0)
        {
            float d = SKPoint.Distance(_points[^1], p);
            if (d < 1.4f)
                return;
            _length += d;
        }

        _points.Add(p);
        TrimFront(MaxLength);
    }

    public void Release(long nowMs) => ReleasedAt = nowMs;

    public bool IsExpired(long nowMs, int fadeMs) =>
        ReleasedAt > 0 && nowMs - ReleasedAt >= fadeMs + 50;

    public void TrimFront(float maxLen)
    {
        while (_points.Count > 2 && _length > maxLen)
        {
            float d = SKPoint.Distance(_points[0], _points[1]);
            _points.RemoveAt(0);
            _length = Math.Max(0, _length - d);
        }
    }
}
