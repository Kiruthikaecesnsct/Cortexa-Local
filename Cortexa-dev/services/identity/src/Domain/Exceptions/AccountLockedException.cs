namespace Cortexa.Identity.Domain.Exceptions;

public sealed class AccountLockedException : Exception
{
    public TimeSpan RetryAfter { get; }

    public AccountLockedException(TimeSpan retryAfter) : base("Account is temporarily locked.")
    {
        RetryAfter = retryAfter;
    }
}
