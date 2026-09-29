namespace Cortexa.Identity.Domain.Entities;

public sealed class FailedLoginAttempt
{
    private FailedLoginAttempt() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int AttemptCount { get; private set; }
    public int LockoutCount { get; private set; }
    public DateTimeOffset WindowStartedAt { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } lockedUntil && lockedUntil > now;

    public TimeSpan RetryAfter(DateTimeOffset now) => IsLocked(now) ? LockedUntil!.Value - now : TimeSpan.Zero;
}
