using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Remote.RateLimit;

public sealed class RateLimitGate : IDisposable
{
    private readonly SourceType _provider;
    private readonly int _minRemaining;
    private readonly RateLimitOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _slots;
    private readonly object _sync = new();
    private int? _remaining;
    private DateTimeOffset? _resetAt;
    private DateTimeOffset? _pausedUntil;
    private RateLimitStatus _published;

    public RateLimitGate(SourceType provider, int minRemaining, RateLimitOptions options, TimeProvider time)
    {
        _provider = provider;
        _minRemaining = minRemaining;
        _options = options;
        _time = time;
        _slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _published = RateLimitStatus.Running(provider);
    }

    public event EventHandler<RateLimitStatus>? StatusChanged;

    public RateLimitStatus Status
    {
        get
        {
            lock (_sync)
            {
                var stale = _published.IsPaused && _published.PausedUntil <= _time.GetUtcNow();
                return (stale ? RateLimitStatus.Running(_provider) : _published) with { Remaining = _remaining };
            }
        }
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            await WaitForClearanceAsync(cancellationToken);
            return new Lease(_slots);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    public void Update(RateSnapshot snapshot)
    {
        if (snapshot.Remaining is not { } remaining)
        {
            return;
        }

        lock (_sync)
        {
            var sameWindow = snapshot.ResetAt == _resetAt && _remaining is not null;
            _remaining = sameWindow ? Math.Min(_remaining!.Value, remaining) : remaining;
            _resetAt = snapshot.ResetAt ?? _resetAt;
        }
    }

    public void Pause(DateTimeOffset until)
    {
        lock (_sync)
        {
            var capped = Min(until, _time.GetUtcNow().AddSeconds(_options.MaxPauseSeconds));
            _pausedUntil = _pausedUntil is { } existing && existing > capped ? existing : capped;
        }
    }

    public void Dispose() => _slots.Dispose();

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private async Task WaitForClearanceAsync(CancellationToken cancellationToken)
    {
        var waited = false;
        try
        {
            while (NextPause() is { } until)
            {
                Publish(new RateLimitStatus(_provider, true, until, null));
                waited = true;
                var delay = until - _time.GetUtcNow();
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, _time, cancellationToken);
                ExpireAfterWait();
            }
        }
        finally
        {
            if (waited)
            {
                Publish(RateLimitStatus.Running(_provider));
            }
        }
    }

    private DateTimeOffset? NextPause()
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            if (_pausedUntil > now)
            {
                return _pausedUntil;
            }

            if (_remaining <= _minRemaining && _resetAt > now)
            {
                return Min(_resetAt.Value, now.AddSeconds(_options.MaxPauseSeconds));
            }

            _remaining = _resetAt <= now ? null : _remaining;
            return null;
        }
    }

    private void ExpireAfterWait()
    {
        lock (_sync)
        {
            _remaining = null;
            _pausedUntil = _pausedUntil <= _time.GetUtcNow() ? null : _pausedUntil;
        }
    }

    private void Publish(RateLimitStatus status)
    {
        lock (_sync)
        {
            if (_published.IsPaused == status.IsPaused && _published.PausedUntil == status.PausedUntil)
            {
                return;
            }

            _published = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
