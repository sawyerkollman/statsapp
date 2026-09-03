namespace Stats.Core.Refresh;

/// <summary>Thread-safe "at most one refresh in flight" latch (v1.8 §10 "Coalescing"). A producer thread (the
/// sensor poller) calls <see cref="TryPost"/> with every new item; it stores the item somewhere the consumer can
/// read (a Volatile/Interlocked field of its own — this type only owns the flag) and schedules the consumer's
/// work (e.g. <c>Dispatcher.BeginInvoke</c>) only when <see cref="TryPost"/> returns true, i.e. only when nothing
/// is already pending. The consumer calls <see cref="Take"/> first, before reading the latest item, so an item
/// that arrives afterward is never silently dropped: it makes the next <see cref="TryPost"/> succeed again.
/// Pure and allocation-free — safe to unit test without a Dispatcher.</summary>
public sealed class RefreshCoalescer
{
    private int _pending;

    /// <summary>Returns true exactly when no refresh is currently pending (and marks one pending) — the caller
    /// should schedule its consumer work. Returns false when one is already pending — the caller's new item will
    /// be picked up by that already-scheduled work instead, so nothing further needs to happen here.</summary>
    public bool TryPost() => Interlocked.CompareExchange(ref _pending, 1, 0) == 0;

    /// <summary>Clears the pending flag. Call this before reading the latest item, not after — clearing first
    /// means an item that arrives between Take() and the read is guaranteed to win a subsequent TryPost() rather
    /// than being silently lost.</summary>
    public void Take() => Interlocked.Exchange(ref _pending, 0);
}
