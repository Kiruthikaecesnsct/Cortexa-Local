using Collector.Application.Auth;
using Collector.Infrastructure.Auth;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Auth;

public class TokenRefreshWorkerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CountingTimeProvider _time = new(Start);
    private readonly FakeSession _session = new();
    private readonly List<TaskCompletionSource> _refreshes = [];

    private TokenRefreshWorker CreateWorker()
    {
        _session.OnRefresh = RotateAsync;
        return new TokenRefreshWorker(
            _session,
            _session,
            TestSupport.Policy(),
            _time,
            NullLogger<TokenRefreshWorker>.Instance);
    }

    private Task RotateAsync()
    {
        _session.ExpiresAt = _time.GetUtcNow() + Lifetime;
        _session.RaiseChanged();
        _refreshes[_session.RefreshCalls - 1].SetResult();
        return Task.CompletedTask;
    }

    private Task NextRefresh()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshes.Add(gate);
        return gate.Task.WaitAsync(Timeout, TestSupport.Ct);
    }

    private void SignIn()
    {
        _session.Current = SessionState.SignedIn;
        _session.ExpiresAt = Start + Lifetime;
        _session.RaiseChanged();
    }

    [Fact]
    public async Task Refreshes_at_expiry_minus_skew_then_re_arms_after_rotation()
    {
        var worker = CreateWorker();
        var first = NextRefresh();
        await worker.StartAsync(TestSupport.Ct);
        SignIn();
        await WaitForTimersAsync(1);

        _time.Advance(Lifetime - Skew - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _session.RefreshCalls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await first;
        Assert.Equal(1, _session.RefreshCalls);

        var second = NextRefresh();
        await WaitForTimersAsync(2);
        _time.Advance(Lifetime - Skew);
        await second;

        Assert.Equal(2, _session.RefreshCalls);
        await worker.StopAsync(TestSupport.Ct);
    }

    [Fact]
    public async Task Does_not_refresh_while_signed_out()
    {
        var worker = CreateWorker();
        await worker.StartAsync(TestSupport.Ct);

        _time.Advance(Lifetime * 2);
        await worker.StopAsync(TestSupport.Ct);

        Assert.Equal(0, _session.RefreshCalls);
    }

    [Fact]
    public async Task Stops_cleanly_on_cancel_while_waiting()
    {
        var worker = CreateWorker();
        await worker.StartAsync(TestSupport.Ct);
        SignIn();
        await WaitForTimersAsync(1);

        await worker.StopAsync(TestSupport.Ct);

        Assert.Equal(0, _session.RefreshCalls);
    }

    private async Task WaitForTimersAsync(int expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (_time.TimersCreated < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestSupport.Ct);
        }

        Assert.True(_time.TimersCreated >= expected);
    }

    private sealed class CountingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _timersCreated;

        public int TimersCreated => Volatile.Read(ref _timersCreated);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timersCreated);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
