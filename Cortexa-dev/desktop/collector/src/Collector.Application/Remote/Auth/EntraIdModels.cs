namespace Collector.Application.Remote.Auth;

public enum EntraFailure
{
    NoWindow,
    Unavailable,
    Other,
}

public abstract record EntraSignInOutcome
{
    private EntraSignInOutcome()
    {
    }

    public sealed record SignedIn(string AccessToken) : EntraSignInOutcome
    {
        public override string ToString() => "SignedIn";
    }

    public sealed record Cancelled : EntraSignInOutcome;

    public sealed record Expired : EntraSignInOutcome;

    public sealed record Denied : EntraSignInOutcome;

    public sealed record Failed(EntraFailure Reason) : EntraSignInOutcome;
}

public sealed record EntraDeviceCode(string UserCode, Uri VerificationUri, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"EntraDeviceCode {{ VerificationUri = {VerificationUri} }}";
}
