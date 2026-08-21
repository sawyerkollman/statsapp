namespace Stats.Core.Metrics;

/// <summary>Fixed-capacity ring buffer of samples plus session-wide min/max/avg (which outlive buffer eviction).</summary>
public sealed class MetricHistory
{
    private readonly float[] _buffer;
    private int _next;
    private int _count;
    private double _sum;
    private long _samples;

    public MetricHistory(int capacity = 120)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new float[capacity];
    }

    public float? Current { get; private set; }
    public float SessionMin { get; private set; } = float.NaN;
    public float SessionMax { get; private set; } = float.NaN;
    public float SessionAvg => _samples == 0 ? float.NaN : (float)(_sum / _samples);

    public void Add(float? value)
    {
        Current = value is float f && float.IsNaN(f) ? null : value;
        if (Current is not float v) return;

        _buffer[_next] = v;
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;

        _sum += v;
        _samples++;
        if (float.IsNaN(SessionMin) || v < SessionMin) SessionMin = v;
        if (float.IsNaN(SessionMax) || v > SessionMax) SessionMax = v;
    }

    /// <summary>Buffered samples, oldest first.</summary>
    public float[] ToArray()
    {
        var result = new float[_count];
        int start = (_next - _count + _buffer.Length) % _buffer.Length;
        for (int i = 0; i < _count; i++)
            result[i] = _buffer[(start + i) % _buffer.Length];
        return result;
    }
}
