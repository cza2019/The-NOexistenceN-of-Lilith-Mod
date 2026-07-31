namespace LilithMod;

internal sealed class StartupUpdateCoordinator
{
    internal const float InitialDelaySeconds = 5f;
    internal const float RetryDelaySeconds = 2f;
    internal const float ReadinessWindowSeconds = 20f;

    private bool _started;
    private bool _pending;
    private bool _completed;
    private float _nextAttempt;
    private float _deadline;

    internal bool Pending => _pending;
    internal bool Completed => _completed;
    internal float NextAttempt => _nextAttempt;

    internal void Start(float now)
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _nextAttempt = now + InitialDelaySeconds;
        _deadline = now + ReadinessWindowSeconds;
    }

    internal bool TryBegin(float now)
    {
        if (!_started || _pending || _completed)
        {
            return false;
        }
        if (now > _deadline)
        {
            _completed = true;
            return false;
        }
        if (now < _nextAttempt)
        {
            return false;
        }
        _pending = true;
        return true;
    }

    internal bool RetryAfterCompanionUnavailable(float now)
    {
        if (!_pending || _completed)
        {
            return false;
        }
        _pending = false;
        if (now >= _deadline)
        {
            _completed = true;
            return false;
        }
        _nextAttempt = System.Math.Min(now + RetryDelaySeconds, _deadline);
        return true;
    }

    internal bool Complete()
    {
        if (!_pending || _completed)
        {
            return false;
        }
        _pending = false;
        _completed = true;
        return true;
    }

    internal void Cancel()
    {
        _pending = false;
        _completed = true;
    }
}
