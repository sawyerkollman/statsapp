namespace Stats.Core.Frames;

/// <summary>PresentMon's CSV header lacks the columns the parser needs (ProcessID + a timing column).</summary>
public sealed class PresentMonFormatException : Exception
{
    public PresentMonFormatException(string message) : base(message) { }
}
