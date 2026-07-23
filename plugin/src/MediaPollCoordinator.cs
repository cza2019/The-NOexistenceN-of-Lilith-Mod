using System.Threading;

namespace LilithMod;

internal sealed class MediaPollCoordinator
{
    private int _inFlight;
    private int _cancelled;
    private int _maxConcurrent;
    private long _begun;
    private long _completed;

    public bool InFlight => Volatile.Read(ref _inFlight) != 0;

    public bool Cancelled => Volatile.Read(ref _cancelled) != 0;

    public int CurrentConcurrent => Volatile.Read(ref _inFlight);

    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    public long Begun => Interlocked.Read(ref _begun);

    public long Completed => Interlocked.Read(ref _completed);

    public bool TryBegin()
    {
        if (Cancelled || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return false;
        }

        Interlocked.Exchange(ref _maxConcurrent, 1);
        Interlocked.Increment(ref _begun);
        if (Cancelled)
        {
            Interlocked.Exchange(ref _inFlight, 0);
            return false;
        }
        return true;
    }

    public bool Complete()
    {
        bool wasInFlight = Interlocked.Exchange(ref _inFlight, 0) != 0;
        if (wasInFlight)
        {
            Interlocked.Increment(ref _completed);
        }
        return wasInFlight && !Cancelled;
    }

    public void Cancel()
    {
        Interlocked.Exchange(ref _cancelled, 1);
    }
}
