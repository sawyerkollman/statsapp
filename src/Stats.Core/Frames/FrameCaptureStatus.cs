namespace Stats.Core.Frames;

public enum FrameCaptureState
{
    Inactive,
    Waiting,
    Collecting,
    Receiving,
    Unavailable,
}

/// <summary>Published capture state for UI status text; safe to read while frames arrive.</summary>
public readonly record struct FrameCaptureStatus(FrameCaptureState State, string Reason);
