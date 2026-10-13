using Collector.Application.Remote.Auth;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote.Auth;

public sealed class DeviceFlowRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GivenInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RealPause = TimeSpan.FromMilliseconds(10);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly ScriptedDeviceClient _client;
    private readonly DeviceFlowRunner _runner;

    public DeviceFlowRunnerTests()
    {
        _client = new ScriptedDeviceClient(_time);
        _runner = new DeviceFlowRunner(_client, _time);
    }

    private Task<DeviceFlowRun> FinishAsync(DeviceCodeGrant grant) => RunToEndAsync(grant, TestSupport.Ct);

    private async Task<DeviceFlowRun> RunToEndAsync(DeviceCodeGrant grant, CancellationToken cancellationToken)
    {
        var task = _runner.RunAsync(RemoteAuthMethod.GitHubDevice, grant, cancellationToken);
        while (!task.IsCompleted)
        {
            _time.Advance(Tick);
            await Task.WhenAny(task, Task.Delay(RealPause, TestSupport.Ct));
        }

        return await task;
    }

    [Fact]
    public async Task RunAsync_PendingThenSuccess_ReturnsSignedInWithTokens()
    {
        var tokens = OAuthData.Tokens("access");
        _client.Enqueue(new DevicePollResult.Pending(), new DevicePollResult.Success(tokens));

        var run = await FinishAsync(OAuthData.Grant());

        Assert.IsType<DeviceFlowOutcome.SignedIn>(run.Outcome);
        Assert.Equal(tokens, run.Tokens);
        Assert.Equal(2, _client.PollTimes.Count);
    }

    [Fact]
    public async Task RunAsync_SlowDownWithoutInterval_AddsFiveSecondsToTheNextWait()
    {
        _client.Enqueue(new DevicePollResult.SlowDown(null), new DevicePollResult.Denied());

        await FinishAsync(OAuthData.Grant(PollInterval));

        var times = _client.PollTimes;
        Assert.Equal(PollInterval, times[0] - Start);
        Assert.Equal(PollInterval + SlowDownStep, times[1] - times[0]);
    }

    [Fact]
    public async Task RunAsync_SlowDownWithInterval_UsesTheReturnedInterval()
    {
        _client.Enqueue(new DevicePollResult.SlowDown(GivenInterval), new DevicePollResult.Denied());

        await FinishAsync(OAuthData.Grant(PollInterval));

        var times = _client.PollTimes;
        Assert.Equal(GivenInterval, times[1] - times[0]);
    }

    [Fact]
    public async Task RunAsync_Expired_ReturnsExpired()
    {
        _client.Enqueue(new DevicePollResult.Expired());

        var run = await FinishAsync(OAuthData.Grant());

        Assert.IsType<DeviceFlowOutcome.Expired>(run.Outcome);
        Assert.Null(run.Tokens);
    }

    [Fact]
    public async Task RunAsync_Denied_ReturnsDenied()
    {
        _client.Enqueue(new DevicePollResult.Denied());

        var run = await FinishAsync(OAuthData.Grant());

        Assert.IsType<DeviceFlowOutcome.Denied>(run.Outcome);
    }

    [Fact]
    public async Task RunAsync_Failed_ReturnsTheTypedReason()
    {
        _client.Enqueue(new DevicePollResult.Failed(DeviceFlowFailure.DeviceFlowDisabled));

        var run = await FinishAsync(OAuthData.Grant());

        var failed = Assert.IsType<DeviceFlowOutcome.Failed>(run.Outcome);
        Assert.Equal(DeviceFlowFailure.DeviceFlowDisabled, failed.Reason);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringTheWait_ReturnsCancelledWithoutPolling()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var task = _runner.RunAsync(RemoteAuthMethod.GitHubDevice, OAuthData.Grant(), cts.Token);

        await cts.CancelAsync();
        var run = await task;

        Assert.IsType<DeviceFlowOutcome.Cancelled>(run.Outcome);
        Assert.Empty(_client.PollTimes);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringThePoll_ReturnsCancelled()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        _client.OnPoll = async token =>
        {
            await cts.CancelAsync();
            token.ThrowIfCancellationRequested();
        };

        var run = await RunToEndAsync(OAuthData.Grant(), cts.Token);

        Assert.IsType<DeviceFlowOutcome.Cancelled>(run.Outcome);
    }

    [Fact]
    public async Task RunAsync_ClockPassesTheDeadline_ReturnsExpiredWithoutAnotherPoll()
    {
        var pending = new DevicePollResult.Pending();
        _client.Enqueue(pending, pending);
        var grant = OAuthData.Grant(PollInterval, TimeSpan.FromSeconds(12));

        var run = await FinishAsync(grant);

        Assert.IsType<DeviceFlowOutcome.Expired>(run.Outcome);
        Assert.Equal(2, _client.PollTimes.Count);
    }
}
