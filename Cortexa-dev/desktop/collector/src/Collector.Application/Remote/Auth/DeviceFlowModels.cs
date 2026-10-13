namespace Collector.Application.Remote.Auth;

public enum DeviceFlowFailure
{
    DeviceFlowDisabled,
    Network,
    Other,
}

public sealed class DeviceFlowException(DeviceFlowFailure reason, Exception? innerException = null)
    : Exception($"GitHub sign-in could not start ({reason}).", innerException)
{
    public DeviceFlowFailure Reason => reason;
}

public sealed record DeviceCodeGrant(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    TimeSpan ExpiresIn,
    TimeSpan Interval)
{
    public override string ToString() => $"DeviceCodeGrant {{ VerificationUri = {VerificationUri} }}";
}

public abstract record DevicePollResult
{
    private DevicePollResult()
    {
    }

    public sealed record Pending : DevicePollResult;

    public sealed record SlowDown(TimeSpan? NewInterval) : DevicePollResult;

    public sealed record Expired : DevicePollResult;

    public sealed record Denied : DevicePollResult;

    public sealed record Success(OAuthTokenSet Tokens) : DevicePollResult;

    public sealed record Failed(DeviceFlowFailure Reason) : DevicePollResult;
}

public abstract record DeviceFlowOutcome
{
    private DeviceFlowOutcome()
    {
    }

    public sealed record SignedIn : DeviceFlowOutcome;

    public sealed record Expired : DeviceFlowOutcome;

    public sealed record Denied : DeviceFlowOutcome;

    public sealed record Cancelled : DeviceFlowOutcome;

    public sealed record Failed(DeviceFlowFailure Reason) : DeviceFlowOutcome;
}

public abstract record RefreshResult
{
    private RefreshResult()
    {
    }

    public sealed record Refreshed(OAuthTokenSet Tokens) : RefreshResult;

    public sealed record Rejected : RefreshResult;

    public sealed record Failed(DeviceFlowFailure Reason) : RefreshResult;
}
