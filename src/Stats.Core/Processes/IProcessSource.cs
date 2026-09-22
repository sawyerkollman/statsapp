namespace Stats.Core.Processes;

public interface IProcessSource : IDisposable
{
    string Name { get; }
    ProcessSourceSnapshot Sample();
    void Reset();
}
