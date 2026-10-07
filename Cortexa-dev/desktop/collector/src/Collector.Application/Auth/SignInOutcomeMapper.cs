namespace Collector.Application.Auth;

internal static class SignInOutcomeMapper
{
    public static SignInOutcome FromFailure(AuthFailure failure) => failure.Kind switch
    {
        AuthFailureKind.InvalidCredentials => new SignInOutcome.InvalidCredentials(),
        AuthFailureKind.Forbidden => new SignInOutcome.Forbidden(),
        AuthFailureKind.AccountLocked => new SignInOutcome.AccountLocked(failure.RetryAfter),
        AuthFailureKind.RateLimited => new SignInOutcome.RateLimited(failure.RetryAfter),
        AuthFailureKind.Unreachable => new SignInOutcome.Unreachable(),
        _ => new SignInOutcome.UnexpectedResponse(),
    };
}
