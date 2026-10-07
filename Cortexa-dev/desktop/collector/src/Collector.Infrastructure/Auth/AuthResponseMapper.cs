using System.Net;
using Collector.Application.Auth;

namespace Collector.Infrastructure.Auth;

public static class AuthResponseMapper
{
    public const string InvalidCredentialsCode = "INVALID_CREDENTIALS";
    public const string InvalidRefreshTokenCode = "INVALID_REFRESH_TOKEN";
    public const string AccountLockedCode = "ACCOUNT_LOCKED";

    public static AuthFailure Map(HttpStatusCode status, string? errorCode, TimeSpan? retryAfter) => status switch
    {
        HttpStatusCode.Unauthorized => MapUnauthorized(errorCode),
        HttpStatusCode.Forbidden => new AuthFailure(AuthFailureKind.Forbidden),
        HttpStatusCode.TooManyRequests => MapTooManyRequests(errorCode, retryAfter),
        HttpStatusCode.BadRequest => new AuthFailure(AuthFailureKind.InvalidInput),
        _ => new AuthFailure(AuthFailureKind.UnexpectedResponse),
    };

    private static AuthFailure MapUnauthorized(string? errorCode) => errorCode switch
    {
        InvalidCredentialsCode => new AuthFailure(AuthFailureKind.InvalidCredentials),
        InvalidRefreshTokenCode => new AuthFailure(AuthFailureKind.InvalidRefreshToken),
        _ => new AuthFailure(AuthFailureKind.UnexpectedResponse),
    };

    private static AuthFailure MapTooManyRequests(string? errorCode, TimeSpan? retryAfter) =>
        errorCode == AccountLockedCode
            ? new AuthFailure(AuthFailureKind.AccountLocked, retryAfter)
            : new AuthFailure(AuthFailureKind.RateLimited, retryAfter);
}
