using System.Reflection;
using Cortexa.Identity.Domain.Entities;

namespace Cortexa.Identity.Application.Tests.Helpers;

internal static class FailedLoginAttemptBuilder
{
    internal static FailedLoginAttempt Build(
        Guid? id = null,
        Guid? userId = null,
        int attemptCount = 1,
        int lockoutCount = 0,
        DateTimeOffset? windowStartedAt = null,
        DateTimeOffset? lastAttemptAt = null,
        DateTimeOffset? lockedUntil = null)
    {
        var attempt = (FailedLoginAttempt)Activator.CreateInstance(typeof(FailedLoginAttempt), nonPublic: true)!;

        SetProperty(attempt, nameof(FailedLoginAttempt.Id), id ?? Guid.NewGuid());
        SetProperty(attempt, nameof(FailedLoginAttempt.UserId), userId ?? Guid.NewGuid());
        SetProperty(attempt, nameof(FailedLoginAttempt.AttemptCount), attemptCount);
        SetProperty(attempt, nameof(FailedLoginAttempt.LockoutCount), lockoutCount);
        SetProperty(attempt, nameof(FailedLoginAttempt.WindowStartedAt), windowStartedAt ?? DateTimeOffset.UtcNow);
        SetProperty(attempt, nameof(FailedLoginAttempt.LastAttemptAt), lastAttemptAt ?? DateTimeOffset.UtcNow);
        SetProperty(attempt, nameof(FailedLoginAttempt.LockedUntil), lockedUntil);

        return attempt;
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = typeof(FailedLoginAttempt).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        var backingField = typeof(FailedLoginAttempt).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (backingField is not null)
            backingField.SetValue(target, value);
        else
            property.SetValue(target, value);
    }
}
