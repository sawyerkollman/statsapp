using System.Globalization;

namespace Stats.Core.Frames;

/// <summary>
/// Header-driven parser for PresentMon console CSV (stdout). Feed it lines in order; the first non-blank
/// line is the header. Tolerates 1.x (`msBetweenPresents`), 2.x (`FrameTime` / `MsBetweenPresents`) naming,
/// and falls back to per-process deltas of `CPUStartTime` (seconds) when no interval column exists.
/// Not thread-safe: call from the stdout reader thread only.
/// </summary>
public sealed class PresentMonCsvParser
{
    private static readonly string[] IntervalColumnNames = { "FrameTime", "MsBetweenPresents" };
    private const string PidColumnName = "ProcessID";
    private const string StartColumnName = "CPUStartTime";

    private int _pidIndex = -1;
    private int _intervalIndex = -1;
    private int _startIndex = -1;
    private int _fieldCount;
    private readonly Dictionary<int, double> _lastStartSeconds = new();

    public bool HeaderParsed { get; private set; }
    /// <summary>Data lines that could not be turned into a sample (wrong width, NA, non-positive, bad pid).</summary>
    public int SkippedLines { get; private set; }

    /// <summary>Returns a sample for a valid data line; null for the header, blank lines, skipped lines, or
    /// a first-seen process in CPUStartTime-fallback mode.</summary>
    /// <exception cref="PresentMonFormatException">Header lacks ProcessID or every timing column.</exception>
    public FrameSample? Parse(string line)
    {
        if (!HeaderParsed)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            ParseHeader(line);
            return null;
        }

        if (string.IsNullOrWhiteSpace(line)) { SkippedLines++; return null; }
        var fields = line.Split(',');
        if (fields.Length != _fieldCount) { SkippedLines++; return null; }

        if (!int.TryParse(fields[_pidIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
        { SkippedLines++; return null; }

        if (_intervalIndex >= 0)
        {
            if (!TryParsePositive(fields[_intervalIndex], out double ms)) { SkippedLines++; return null; }
            return new FrameSample(pid, ms);
        }

        // CPUStartTime fallback: interval = delta of this process's consecutive start times.
        if (!double.TryParse(fields[_startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double startSec)
            || double.IsNaN(startSec))
        { SkippedLines++; return null; }
        if (_lastStartSeconds.TryGetValue(pid, out double prev))
        {
            _lastStartSeconds[pid] = startSec;
            double ms = (startSec - prev) * 1000.0;
            if (ms <= 0) { SkippedLines++; return null; }
            return new FrameSample(pid, ms);
        }
        _lastStartSeconds[pid] = startSec;
        return null;
    }

    private void ParseHeader(string line)
    {
        var names = line.Split(',');
        _fieldCount = names.Length;
        _pidIndex = IndexOf(names, PidColumnName);
        if (_pidIndex < 0)
            throw new PresentMonFormatException($"PresentMon CSV header has no '{PidColumnName}' column: {line}");
        foreach (var candidate in IntervalColumnNames)
        {
            _intervalIndex = IndexOf(names, candidate);
            if (_intervalIndex >= 0) break;
        }
        if (_intervalIndex < 0)
        {
            _startIndex = IndexOf(names, StartColumnName);
            if (_startIndex < 0)
                throw new PresentMonFormatException(
                    $"PresentMon CSV header has none of FrameTime/MsBetweenPresents/CPUStartTime: {line}");
        }
        HeaderParsed = true;
    }

    private static int IndexOf(string[] names, string wanted)
    {
        for (int i = 0; i < names.Length; i++)
            if (string.Equals(names[i].Trim(), wanted, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
}
