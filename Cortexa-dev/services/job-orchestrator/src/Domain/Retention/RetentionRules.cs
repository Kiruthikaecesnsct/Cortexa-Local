namespace Cortexa.JobOrchestrator.Domain.Retention;

public static class RetentionRules
{
    public static bool IsFailedExpired(
        DateTimeOffset? failedAt,
        DateTimeOffset? createdAt,
        DateTimeOffset nowUtc,
        int retentionDays)
    {
        var reference = failedAt ?? createdAt;
        if (reference is null)
            return false;

        return nowUtc - reference.Value >= TimeSpan.FromDays(retentionDays);
    }

    public static bool IsIncompleteStuck(
        DateTimeOffset? createdAt,
        DateTimeOffset lastActivityUtc,
        DateTimeOffset nowUtc,
        int stuckHours)
    {
        var reference = createdAt ?? lastActivityUtc;
        return nowUtc - reference >= TimeSpan.FromHours(stuckHours);
    }

    public static bool IsWedgedRunning(
        DateTimeOffset lastActivityUtc,
        DateTimeOffset nowUtc,
        int stuckHours,
        bool forceEnabled)
    {
        if (!forceEnabled)
            return false;

        return nowUtc - lastActivityUtc >= TimeSpan.FromHours(stuckHours);
    }
}
