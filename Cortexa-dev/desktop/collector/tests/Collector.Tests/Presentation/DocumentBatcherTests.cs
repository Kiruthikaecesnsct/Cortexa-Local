using Collector.Application.Extraction;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class DocumentBatcherTests
{
    private static readonly TimeSpan JustUnderInterval = IntakeTiming.FlushInterval - TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly List<IReadOnlyList<SplitOutcome>> _flushes = [];

    private DocumentBatcher CreateBatcher() => new(_time, _flushes.Add);

    private static SplitOutcome Outcome(string name) => SplitOutcomes.Make(name);

    [Fact]
    public void Enqueue_BeforeTheIntervalElapses_DoesNotFlush()
    {
        using var batcher = CreateBatcher();

        batcher.Enqueue(Outcome("a.md"));
        _time.Advance(JustUnderInterval);

        Assert.Empty(_flushes);
        Assert.Equal(1, batcher.PendingCount);
    }

    [Fact]
    public void Enqueue_IntervalElapses_FlushesAllPendingOutcomesInOneBatch()
    {
        using var batcher = CreateBatcher();
        batcher.Enqueue(Outcome("a.md"));
        batcher.Enqueue(Outcome("b.md"));

        _time.Advance(IntakeTiming.FlushInterval);

        var batch = Assert.Single(_flushes);
        Assert.Equal(["a.md", "b.md"], batch.Select(outcome => outcome.Result.SourcePath));
        Assert.Equal(0, batcher.PendingCount);
    }

    [Fact]
    public void Tick_NothingPending_DoesNotFlush()
    {
        using var batcher = CreateBatcher();

        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Empty(_flushes);
    }

    [Fact]
    public void Tick_SecondIntervalWithNewOutcome_FlushesASecondBatch()
    {
        using var batcher = CreateBatcher();
        batcher.Enqueue(Outcome("a.md"));
        _time.Advance(IntakeTiming.FlushInterval);
        batcher.Enqueue(Outcome("b.md"));

        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(2, _flushes.Count);
        Assert.Equal("b.md", Assert.Single(_flushes[1]).Result.SourcePath);
    }

    [Fact]
    public void FlushNow_PendingOutcomes_FlushesWithoutWaitingForTheTimer()
    {
        using var batcher = CreateBatcher();
        batcher.Enqueue(Outcome("a.md"));

        batcher.FlushNow();

        Assert.Single(Assert.Single(_flushes));
        Assert.Equal(0, batcher.PendingCount);
    }

    [Fact]
    public void FlushNow_NothingPending_SkipsTheCallback()
    {
        using var batcher = CreateBatcher();

        batcher.FlushNow();

        Assert.Empty(_flushes);
    }

    [Fact]
    public void Dispose_PendingOutcomes_AreNotFlushedByLaterTicks()
    {
        var batcher = CreateBatcher();
        batcher.Enqueue(Outcome("a.md"));

        batcher.Dispose();
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Empty(_flushes);
    }
}
