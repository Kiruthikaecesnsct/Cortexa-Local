namespace Collector.Application.Auth;

public enum SignInField
{
    Email,
    Password,
}

public abstract record SignInOutcome
{
    private SignInOutcome()
    {
    }

    public sealed record Success : SignInOutcome;

    public sealed record InvalidInput(SignInField Field, string Reason) : SignInOutcome;

    public sealed record InvalidCredentials : SignInOutcome;

    public sealed record Forbidden : SignInOutcome;

    public sealed record AccountLocked(TimeSpan? RetryAfter) : SignInOutcome;

    public sealed record RateLimited(TimeSpan? RetryAfter) : SignInOutcome;

    public sealed record Unreachable : SignInOutcome;

    public sealed record UnexpectedResponse : SignInOutcome;
}
