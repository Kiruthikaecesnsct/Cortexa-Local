using Cortexa.Identity.Application.Tests.Helpers;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Entities;

public sealed class FailedLoginAttemptTests
{
    [Fact]
    public void IsLocked_WhenLockedUntilIsNull_ReturnsFalse()
    {
        var attempt = FailedLoginAttemptBuilder.Build(lockedUntil: null);

        Assert.False(attempt.IsLocked(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsLocked_WhenLockedUntilIsInFuture_ReturnsTrue()
    {
        var attempt = FailedLoginAttemptBuilder.Build(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.True(attempt.IsLocked(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsLocked_WhenLockedUntilIsInPast_ReturnsFalse()
    {
        var attempt = FailedLoginAttemptBuilder.Build(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.False(attempt.IsLocked(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RetryAfter_WhenLocked_ReturnsRemainingDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = FailedLoginAttemptBuilder.Build(lockedUntil: now.AddSeconds(30));

        var retryAfter = attempt.RetryAfter(now);

        Assert.True(retryAfter <= TimeSpan.FromSeconds(30));
        Assert.True(retryAfter > TimeSpan.FromSeconds(29));
    }

    [Fact]
    public void RetryAfter_WhenNotLocked_ReturnsZero()
    {
        var attempt = FailedLoginAttemptBuilder.Build(lockedUntil: null);

        Assert.Equal(TimeSpan.Zero, attempt.RetryAfter(DateTimeOffset.UtcNow));
    }
}
