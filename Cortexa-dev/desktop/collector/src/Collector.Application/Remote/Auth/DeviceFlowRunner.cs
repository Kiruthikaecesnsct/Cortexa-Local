using Collector.Application.Ports;

namespace Collector.Application.Remote.Auth;

public sealed record DeviceFlowRun(DeviceFlowOutcome Outcome, OAuthTokenSet? Tokens = null);

public sealed class DeviceFlowRunner(IDeviceAuthorizationClient client, TimeProvider time)
{
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    public Task<DeviceCodeGrant> RequestCodeAsync(RemoteAuthMethod method, CancellationToken cancellationToken) =>
        client.RequestCodeAsync(method, cancellationToken);

    public async Task<DeviceFlowRun> RunAsync(
        RemoteAuthMethod method,
        DeviceCodeGrant grant,
        CancellationToken cancellationToken)
    {
        var interval = grant.Interval;
        var deadline = time.GetUtcNow() + grant.ExpiresIn;
        while (true)
        {
            if (!await TryWaitAsync(interval, cancellationToken))
            {
                return new DeviceFlowRun(new DeviceFlowOutcome.Cancelled());
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new DeviceFlowRun(new DeviceFlowOutcome.Expired());
            }

            var result = await TryPollAsync(method, grant.DeviceCode, cancellationToken);
            if (result is null)
            {
                return new DeviceFlowRun(new DeviceFlowOutcome.Cancelled());
            }

            if (Conclude(result) is { } finished)
            {
                return finished;
            }

            interval = NextInterval(result, interval);
        }
    }

    private static DeviceFlowRun? Conclude(DevicePollResult result) => result switch
    {
        DevicePollResult.Success success => new DeviceFlowRun(new DeviceFlowOutcome.SignedIn(), success.Tokens),
        DevicePollResult.Expired => new DeviceFlowRun(new DeviceFlowOutcome.Expired()),
        DevicePollResult.Denied => new DeviceFlowRun(new DeviceFlowOutcome.Denied()),
        DevicePollResult.Failed failed => new DeviceFlowRun(new DeviceFlowOutcome.Failed(failed.Reason)),
        _ => null,
    };

    private static TimeSpan NextInterval(DevicePollResult result, TimeSpan current) =>
        result is DevicePollResult.SlowDown slowDown ? slowDown.NewInterval ?? current + SlowDownStep : current;

    private async Task<bool> TryWaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(interval, time, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<DevicePollResult?> TryPollAsync(
        RemoteAuthMethod method,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.PollAsync(method, deviceCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
