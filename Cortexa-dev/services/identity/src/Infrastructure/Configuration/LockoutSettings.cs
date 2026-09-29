namespace Cortexa.Identity.Infrastructure.Configuration;

public sealed class LockoutSettings
{
    public int MaxAttempts { get; set; } = 5;
    public int WindowSeconds { get; set; } = 900;
    public int BaseLockoutSeconds { get; set; } = 30;
    public int MaxLockoutSeconds { get; set; } = 3600;
}
