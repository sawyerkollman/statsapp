namespace Stats.Core.Metrics;

/// <summary>Fixed-capacity ring buffer of samples plus session-wide min/max/avg (which outlive buffer eviction).</summary>
public sealed class MetricHistory
{
    private float[] _buffer;
    private int _next;
    private int _count;
    private double _sum;
    private long _samples;

    public MetricHistory(int capacity = 120)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;
    public float? Current { get; private set; }
    public float SessionMin { get; private set; } = float.NaN;
    public float SessionMax { get; private set; } = float.NaN;
    public float SessionAvg => _samples == 0 ? float.NaN : (float)(_sum / _samples);
    /// <summary>UTC instant SessionMin was last set; null until the first real sample, cleared by ResetSession.</summary>
    public DateTime? SessionMinAtUtc { get; private set; }
    /// <summary>UTC instant SessionMax was last set; null until the first real sample, cleared by ResetSession.</summary>
    public DateTime? SessionMaxAtUtc { get; private set; }

    public void Add(float? value) => Add(value, DateTime.UtcNow);

    /// <summary>Always advances the ring buffer by one slot — a null/NaN sample stores float.NaN so every slot
    /// represents one poll tick and the buffer's x-axis stays uniform in time. Current stays null for the gap;
    /// session min/max/avg (and their timestamps) are left untouched since they only ever reflect real samples.</summary>
    public void Add(float? value, DateTime timestampUtc)
    {
        Current = value is float f && float.IsNaN(f) ? null : value;

        _buffer[_next] = Current ?? float.NaN;
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;

        if (Current is not float v) return;

        _sum += v;
        _samples++;
        if (float.IsNaN(SessionMin) || v < SessionMin) { SessionMin = v; SessionMinAtUtc = timestampUtc; }
        if (float.IsNaN(SessionMax) || v > SessionMax) { SessionMax = v; SessionMaxAtUtc = timestampUtc; }
    }

    /// <summary>Buffered samples, oldest first. Always allocates — callers that refresh every tick and want to
    /// avoid steady-state allocation should use <see cref="CopyTo"/> instead.</summary>
    public float[] ToArray() => CopyTo(null);

    /// <summary>Buffered samples, oldest first, written into <paramref name="reuse"/> when its length already
    /// matches the current sample count (a fresh array is allocated otherwise — capacity not yet reached, just
    /// resized, or <paramref name="reuse"/> is null). Content is identical to <see cref="ToArray"/>, NaN gaps
    /// included.</summary>
    public float[] CopyTo(float[]? reuse)
    {
        var result = reuse is not null && reuse.Length == _count ? reuse : new float[_count];
        int start = (_next - _count + _buffer.Length) % _buffer.Length;
        for (int i = 0; i < _count; i++)
            result[i] = _buffer[(start + i) % _buffer.Length];
        return result;
    }

    /// <summary>Change buffer capacity, keeping the newest samples. Session stats unaffected.</summary>
    public void Resize(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity == _buffer.Length) return;
        var keep = ToArray();
        int take = Math.Min(keep.Length, capacity);
        _buffer = new float[capacity];
        Array.Copy(keep, keep.Length - take, _buffer, 0, take);
        _count = take;
        _next = take % capacity;
    }

    /// <summary>Clear buffer and session stats. Current value is kept for display continuity.</summary>
    public void ResetSession()
    {
        Array.Clear(_buffer);
        _count = 0;
        _next = 0;
        _sum = 0;
        _samples = 0;
        SessionMin = float.NaN;
        SessionMax = float.NaN;
        SessionMinAtUtc = null;
        SessionMaxAtUtc = null;
    }
}
