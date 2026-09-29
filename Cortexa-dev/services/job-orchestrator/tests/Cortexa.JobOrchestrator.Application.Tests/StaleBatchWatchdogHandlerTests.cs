using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class StaleBatchWatchdogHandlerTests
{
    private readonly ISagaRepository _repository = Substitute.For<ISagaRepository>();
    private readonly ILogger<StaleBatchWatchdogHandler> _logger = Substitute.For<ILogger<StaleBatchWatchdogHandler>>();

    private StaleBatchWatchdogHandler BuildHandler(WatchdogOptions options) =>
        new(_repository, Options.Create(options), _logger);

    private static BatchSaga Saga(string batchId, BatchState state) =>
        new(batchId, state, [], wantsHarvesting: false, wantsSeeding: false, version: 1,
            eTag: "\"etag-1\"", schemaVersion: 1);

    [Fact]
    public async Task RunAsync_StalledInProgressBatch_MarksFailedAndPersists()
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 30 });
        var saga = Saga("batch-stalled", BatchState.InProgress);

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "batch-stalled" });
        _repository.GetAsync("batch-stalled", Arg.Any<CancellationToken>()).Returns(saga);

        var failedCount = await handler.RunAsync(CancellationToken.None);

        failedCount.Should().Be(1);
        saga.State.Should().Be(BatchState.Failed);
        saga.FailureReason.Should().Contain("30 minutes");
        await _repository.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoStalledBatches_ReturnsZeroWithoutUpdating()
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 30 });

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var failedCount = await handler.RunAsync(CancellationToken.None);

        failedCount.Should().Be(0);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    [InlineData(BatchState.Queued)]
    public async Task RunAsync_BatchNoLongerInProgress_LeavesSagaUntouched(BatchState currentState)
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 30 });
        var saga = Saga("batch-raced", currentState);

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "batch-raced" });
        _repository.GetAsync("batch-raced", Arg.Any<CancellationToken>()).Returns(saga);

        var failedCount = await handler.RunAsync(CancellationToken.None);

        failedCount.Should().Be(0);
        saga.State.Should().Be(currentState);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SagaNoLongerExists_SkipsGracefully()
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 30 });

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "batch-gone" });
        _repository.GetAsync("batch-gone", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        var failedCount = await handler.RunAsync(CancellationToken.None);

        failedCount.Should().Be(0);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_QueriesCutoffDerivedFromStageStallSlaMinutes()
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 45 });

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var before = DateTimeOffset.UtcNow;
        await handler.RunAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        var expectedEarliest = before.AddMinutes(-45);
        var expectedLatest = after.AddMinutes(-45);

        await _repository.Received(1).ListStalledBatchIdsAsync(
            Arg.Is<DateTimeOffset>(c => c >= expectedEarliest && c <= expectedLatest),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MultipleStalledBatches_ProcessesEachIndependently()
    {
        var handler = BuildHandler(new WatchdogOptions { StageStallSlaMinutes = 30 });
        var stalledSaga = Saga("batch-a", BatchState.InProgress);
        var racedSaga = Saga("batch-b", BatchState.Completed);

        _repository.ListStalledBatchIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "batch-a", "batch-b" });
        _repository.GetAsync("batch-a", Arg.Any<CancellationToken>()).Returns(stalledSaga);
        _repository.GetAsync("batch-b", Arg.Any<CancellationToken>()).Returns(racedSaga);

        var failedCount = await handler.RunAsync(CancellationToken.None);

        failedCount.Should().Be(1);
        stalledSaga.State.Should().Be(BatchState.Failed);
        racedSaga.State.Should().Be(BatchState.Completed);
        await _repository.Received(1).UpdateAsync(stalledSaga, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpdateAsync(racedSaga, Arg.Any<CancellationToken>());
    }
}
