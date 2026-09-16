namespace Stats.Core.Processes;

public sealed class SlowSampleGate
{
    private readonly TimeSpan _threshold;
    private readonly int _consecutiveSamples;
    private int _slowSamples;

    public SlowSampleGate(TimeSpan threshold, int consecutiveSamples)
    {
        if (consecutiveSamples <= 0) throw new ArgumentOutOfRangeException(nameof(consecutiveSamples));
        _threshold = threshold;
        _consecutiveSamples = consecutiveSamples;
    }

    public bool IsTripped { get; private set; }
    public bool Record(TimeSpan elapsed)
    {
        if (IsTripped) return true;
        _slowSamples = elapsed > _threshold ? _slowSamples + 1 : 0;
        return IsTripped = _slowSamples >= _consecutiveSamples;
    }
}
