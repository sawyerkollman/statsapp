namespace Stats.Core.Frames;

/// <summary>A foreground-qualified PresentMon frame, timestamped when Stats received it.</summary>
public readonly record struct RecordedFrame(DateTime TimestampUtc, int Pid, double FrameTimeMs);
