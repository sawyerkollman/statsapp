namespace Stats.Core.Frames;

/// <summary>One presented frame: which process, and how long since that process's previous present.</summary>
public readonly record struct FrameSample(int Pid, double FrameTimeMs);
