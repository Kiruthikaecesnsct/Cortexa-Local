using Microsoft.Extensions.Options;

namespace Collector.Application.Auth;

public sealed class TokenRefreshPolicy
{
    private readonly TimeSpan _skew;
    private readonly TimeSpan _minDelay;

    public TokenRefreshPolicy(IOptions<AuthOptions> options)
    {
        _skew = TimeSpan.FromSeconds(options.Value.RefreshSkewSeconds);
        _minDelay = TimeSpan.FromSeconds(options.Value.MinRefreshDelaySeconds);
    }

    public DateTimeOffset NextRefreshAt(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var due = expiresAt - _skew;
        var floor = now + _minDelay;
        return due > floor ? due : floor;
    }

    public bool IsRefreshDue(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt - _skew;
}
