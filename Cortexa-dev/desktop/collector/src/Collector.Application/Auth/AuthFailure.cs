namespace Collector.Application.Auth;

public enum AuthFailureKind
{
    InvalidCredentials,
    Forbidden,
    AccountLocked,
    RateLimited,
    InvalidRefreshToken,
    InvalidInput,
    Unreachable,
    UnexpectedResponse,
}

public sealed record AuthFailure(AuthFailureKind Kind, TimeSpan? RetryAfter = null);
