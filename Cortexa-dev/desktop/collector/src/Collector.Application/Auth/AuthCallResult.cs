namespace Collector.Application.Auth;

public sealed record AuthCallResult
{
    private AuthCallResult(AuthTokens? tokens, AuthFailure? failure)
    {
        Tokens = tokens;
        Failure = failure;
    }

    public AuthTokens? Tokens { get; }

    public AuthFailure? Failure { get; }

    public bool IsSuccess => Tokens is not null;

    public static AuthCallResult Success(AuthTokens tokens) => new(tokens, null);

    public static AuthCallResult Fail(AuthFailure failure) => new(null, failure);

    public static AuthCallResult Fail(AuthFailureKind kind, TimeSpan? retryAfter = null) =>
        new(null, new AuthFailure(kind, retryAfter));
}
