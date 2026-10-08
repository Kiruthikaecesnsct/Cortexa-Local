using Collector.Infrastructure.Options;
using Collector.Presentation.Services;
using Collector.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class HistoryPollerTests : IDisposable
{
    private const int Seconds = 5;

    private readonly FakeTimeProvider _time = new();
    private readonly List<HistoryPoller> _pollers = [];
    private int _ticks;

    public void Dispose()
    {
        foreach (var poller in _pollers)
        {
            poller.Dispose();
        }
    }

    private HistoryPoller Create(int seconds = Seconds)
    {
        var poller = new HistoryPoller(_time, Options.Create(new HistoryOptions { PollSeconds = seconds }));
        _pollers.Add(poller);
        return poller;
    }

    private Task CountTick(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _ticks);
        return Task.CompletedTask;
    }

    private async Task AdvanceAsync(int seconds, int expectedTicks)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        await HistoryHarness.Eventually(() => Volatile.Read(ref _ticks) >= expectedTicks);
    }

    [Fact]
    public async Task Start_TicksOncePerInterval()
    {
        var poller = Create();
        poller.Start(() => true, CountTick);

        await AdvanceAsync(Seconds, 1);
        await AdvanceAsync(Seconds, 2);

        Assert.Equal(2, _ticks);
    }

    [Fact]
    public async Task Start_UsesConfiguredInterval()
    {
        var poller = Create(seconds: 2);
        poller.Start(() => true, CountTick);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, TestSupport.Ct);
        Assert.Equal(0, _ticks);

        await AdvanceAsync(1, 1);
    }

    [Fact]
    public async Task Start_CalledTwice_RunsOneLoop()
    {
        var poller = Create();
        poller.Start(() => true, CountTick);
        poller.Start(() => true, CountTick);

        await AdvanceAsync(Seconds, 1);
        await Task.Delay(50, TestSupport.Ct);

        Assert.Equal(1, _ticks);
    }

    [Fact]
    public async Task Stop_EndsTicking_AndIsIdempotent()
    {
        var poller = Create();
        poller.Start(() => true, CountTick);
        await AdvanceAsync(Seconds, 1);

        poller.Stop();
        poller.Stop();
        _time.Advance(TimeSpan.FromSeconds(Seconds * 2));
        await Task.Delay(50, TestSupport.Ct);

        Assert.False(poller.IsRunning);
        Assert.Equal(1, _ticks);
    }

    [Fact]
    public void Stop_BeforeStart_DoesNothing()
    {
        var poller = Create();

        poller.Stop();

        Assert.False(poller.IsRunning);
    }

    [Fact]
    public async Task Start_AfterStop_RestartsTicking()
    {
        var poller = Create();
        poller.Start(() => true, CountTick);
        poller.Stop();

        poller.Start(() => true, CountTick);
        await AdvanceAsync(Seconds, 1);

        Assert.True(poller.IsRunning);
    }

    [Fact]
    public async Task ShouldPollFalse_EndsLoopWithoutTicking()
    {
        var poller = Create();
        poller.Start(() => false, CountTick);

        _time.Advance(TimeSpan.FromSeconds(Seconds));
        await HistoryHarness.Eventually(() => !poller.IsRunning);

        Assert.Equal(0, _ticks);
    }

    [Fact]
    public async Task SlowTick_NeverOverlaps()
    {
        var poller = Create();
        var gate = new TaskCompletionSource();
        var running = 0;
        var maxRunning = 0;
        var started = 0;
        poller.Start(() => true, async _ =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            Interlocked.Increment(ref started);
            await gate.Task;
            Interlocked.Decrement(ref running);
        });

        _time.Advance(TimeSpan.FromSeconds(Seconds));
        await HistoryHarness.Eventually(() => Volatile.Read(ref started) == 1);
        _time.Advance(TimeSpan.FromSeconds(Seconds * 3));
        await Task.Delay(50, TestSupport.Ct);
        var startedWhileBlocked = Volatile.Read(ref started);
        gate.SetResult();
        await HistoryHarness.Eventually(() => Volatile.Read(ref running) == 0);

        Assert.Equal(1, startedWhileBlocked);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task StopInsideTick_EndsLoop()
    {
        var poller = Create();
        poller.Start(() => true, _ =>
        {
            poller.Stop();
            return Task.CompletedTask;
        });

        _time.Advance(TimeSpan.FromSeconds(Seconds));
        await HistoryHarness.Eventually(() => !poller.IsRunning);
        _time.Advance(TimeSpan.FromSeconds(Seconds));

        Assert.False(poller.IsRunning);
    }
}
