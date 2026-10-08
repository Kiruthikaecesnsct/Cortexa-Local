using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote.RateLimit;

public sealed class RateLimitGateTests : IDisposable
{
    private const int MinRemaining = 10;
    private const int MaxPauseSeconds = 900;
    private const int LowRemaining = 5;
    private const int HighRemaining = 20;
    private const int ResetInSeconds = 60;
    private const int OneHourSeconds = 3600;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly List<RateLimitGate> _gates = [];

    public void Dispose()
    {
        foreach (var gate in _gates)
        {
            gate.Dispose();
        }
    }

    private RateLimitGate CreateGate(int concurrency = 4)
    {
        var options = new RateLimitOptions { MaxConcurrency = concurrency, MaxPauseSeconds = MaxPauseSeconds };
        var gate = new RateLimitGate(SourceType.Github, MinRemaining, options, _time);
        _gates.Add(gate);
        return gate;
    }

    private static Task Completed(Task<IDisposable> pending) => pending.WaitAsync(Patience, TestSupport.Ct);

    [Fact]
    public async Task EnterAsync_NoLimitKnown_CompletesImmediately()
    {
        var gate = CreateGate();

        using var lease = await gate.EnterAsync(TestSupport.Ct);

        Assert.False(gate.Status.IsPaused);
    }

    [Fact]
    public async Task EnterAsync_RemainingAtMinimum_WaitsUntilReset()
    {
        var gate = CreateGate();
        gate.Update(new RateSnapshot(LowRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds), null));

        var pending = gate.EnterAsync(TestSupport.Ct);

        Assert.False(pending.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(ResetInSeconds));
        await Completed(pending);
        (await pending).Dispose();
    }

    [Fact]
    public async Task EnterAsync_RemainingAboveMinimum_DoesNotWait()
    {
        var gate = CreateGate();
        gate.Update(new RateSnapshot(HighRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds), null));

        using var lease = await gate.EnterAsync(TestSupport.Ct);

        Assert.Equal(HighRemaining, gate.Status.Remaining);
    }

    [Fact]
    public async Task EnterAsync_AfterWait_PublishesPausedThenRunning()
    {
        var gate = CreateGate();
        var statuses = new List<RateLimitStatus>();
        gate.StatusChanged += (_, status) => statuses.Add(status);
        gate.Update(new RateSnapshot(LowRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds), null));

        var pending = gate.EnterAsync(TestSupport.Ct);
        _time.Advance(TimeSpan.FromSeconds(ResetInSeconds));
        (await pending.WaitAsync(Patience, TestSupport.Ct)).Dispose();

        Assert.Equal([true, false], statuses.Select(status => status.IsPaused));
    }

    [Fact]
    public async Task EnterAsync_CancelledWhilePaused_ReportsRunningAfterwards()
    {
        var gate = CreateGate();
        using var cts = new CancellationTokenSource();
        gate.Update(new RateSnapshot(LowRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds), null));
        var pending = gate.EnterAsync(cts.Token);
        Assert.True(gate.Status.IsPaused);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Patience, TestSupport.Ct));
        Assert.False(gate.Status.IsPaused);
    }

    [Fact]
    public async Task Pause_BeyondMaxPause_IsCappedToMaxPause()
    {
        var gate = CreateGate();
        gate.Pause(_time.GetUtcNow().AddSeconds(OneHourSeconds));

        var pending = gate.EnterAsync(TestSupport.Ct);
        _time.Advance(TimeSpan.FromSeconds(MaxPauseSeconds));

        (await pending.WaitAsync(Patience, TestSupport.Ct)).Dispose();
        Assert.False(gate.Status.IsPaused);
    }

    [Fact]
    public async Task Pause_ShorterThanExisting_KeepsLongerPause()
    {
        var gate = CreateGate();
        gate.Pause(_time.GetUtcNow().AddSeconds(ResetInSeconds));
        gate.Pause(_time.GetUtcNow().AddSeconds(1));

        var pending = gate.EnterAsync(TestSupport.Ct);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(pending.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(ResetInSeconds));
        (await pending.WaitAsync(Patience, TestSupport.Ct)).Dispose();
    }

    [Fact]
    public void Update_NullRemaining_LeavesStatusUnchanged()
    {
        var gate = CreateGate();
        gate.Update(new RateSnapshot(HighRemaining, null, null));

        gate.Update(RateSnapshot.Empty);

        Assert.Equal(HighRemaining, gate.Status.Remaining);
    }

    [Fact]
    public void Update_SameWindow_KeepsLowestRemaining()
    {
        var gate = CreateGate();
        var reset = _time.GetUtcNow().AddSeconds(ResetInSeconds);
        gate.Update(new RateSnapshot(HighRemaining, reset, null));

        gate.Update(new RateSnapshot(HighRemaining + 5, reset, null));

        Assert.Equal(HighRemaining, gate.Status.Remaining);
    }

    [Fact]
    public void Update_NewWindow_ReplacesRemaining()
    {
        var gate = CreateGate();
        gate.Update(new RateSnapshot(LowRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds), null));

        gate.Update(new RateSnapshot(HighRemaining, _time.GetUtcNow().AddSeconds(ResetInSeconds * 2), null));

        Assert.Equal(HighRemaining, gate.Status.Remaining);
    }

    [Fact]
    public async Task EnterAsync_ConcurrencyExhausted_WaitsForLeaseRelease()
    {
        var gate = CreateGate(concurrency: 1);
        var first = await gate.EnterAsync(TestSupport.Ct);

        var second = gate.EnterAsync(TestSupport.Ct);

        Assert.False(second.IsCompleted);
        first.Dispose();
        (await second.WaitAsync(Patience, TestSupport.Ct)).Dispose();
    }

    [Fact]
    public async Task Lease_DisposedTwice_ReleasesOnlyOneSlot()
    {
        var gate = CreateGate(concurrency: 1);
        var first = await gate.EnterAsync(TestSupport.Ct);
        first.Dispose();
        first.Dispose();
        var second = await gate.EnterAsync(TestSupport.Ct);

        var third = gate.EnterAsync(TestSupport.Ct);

        Assert.False(third.IsCompleted);
        second.Dispose();
        (await third.WaitAsync(Patience, TestSupport.Ct)).Dispose();
    }

    [Fact]
    public async Task EnterAsync_CancelledWhilePaused_ReleasesTheSlot()
    {
        var gate = CreateGate(concurrency: 1);
        gate.Pause(_time.GetUtcNow().AddSeconds(ResetInSeconds));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var cancelled = gate.EnterAsync(cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        _time.Advance(TimeSpan.FromSeconds(ResetInSeconds));
        (await gate.EnterAsync(TestSupport.Ct).WaitAsync(Patience, TestSupport.Ct)).Dispose();
    }
}
