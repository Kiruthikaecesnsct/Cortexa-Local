using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class RetentionSweepHandlerTests
{
    private const string FailedState = "Failed";
    private const string QueuedState = "Queued";
    private const string InProgressState = "InProgress";
    private const int WedgedHours = 72;

    private readonly IBatchRetentionQuery _query = Substitute.For<IBatchRetentionQuery>();
    private readonly ISagaRepository _sagas = Substitute.For<ISagaRepository>();
    private readonly IBatchDeleter _deleter = Substitute.For<IBatchDeleter>();
    private readonly ILogger<DeleteBatchHandler> _deleteLogger = Substitute.For<ILogger<DeleteBatchHandler>>();
    private readonly ILogger<RetentionSweepHandler> _sweepLogger = Substitute.For<ILogger<RetentionSweepHandler>>();

    private static RetentionCandidate FailedCandidate(string batchId) =>
        new(batchId, FailedState, DateTimeOffset.UtcNow.AddDays(-31), DateTimeOffset.UtcNow.AddDays(-31),
            DateTimeOffset.UtcNow.AddDays(-31), "Failed retention exceeded");

    private static RetentionCandidate QueuedCandidate(string batchId) =>
        new(batchId, QueuedState, DateTimeOffset.UtcNow.AddDays(-5), null,
            DateTimeOffset.UtcNow.AddDays(-5), "Queued batch stuck");

    private static RetentionCandidate InProgressCandidate(string batchId) =>
        new(batchId, InProgressState, DateTimeOffset.UtcNow.AddHours(-(WedgedHours + 1)), null,
            DateTimeOffset.UtcNow.AddHours(-(WedgedHours + 1)), "InProgress batch wedged");

    private static BatchSaga Saga(string batchId, BatchState state) =>
        new(batchId, state, [], wantsHarvesting: false, wantsSeeding: false, version: 1,
            eTag: "\"e1\"", schemaVersion: 1,
            activeDocumentIds: new HashSet<string>(), queuedDocumentIds: new Queue<string>());

    private RetentionSweepHandler BuildHandler(RetentionPolicyOptions options)
    {
        var deleteHandler = new DeleteBatchHandler(_sagas, [_deleter], _deleteLogger);
        return new RetentionSweepHandler(_query, deleteHandler, Options.Create(options), _sweepLogger);
    }

    private void SetupDeleterSuccess(string storeName = "Cosmos")
    {
        _deleter.StoreName.Returns(storeName);
        _deleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoreDeletionResult(storeName, 1, true));
    }

    [Fact]
    public async Task SweepAsync_DryRun_ProducesReportWithoutDeletingAnything()
    {
        var candidate = FailedCandidate("batch-dry-1");
        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { candidate });
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = true });

        var report = await handler.SweepAsync(null, CancellationToken.None);

        report.DryRun.Should().BeTrue();
        report.DeletedCount.Should().Be(0);
        report.Candidates.Should().HaveCount(1);
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_ExecuteMode_DeletesFailedAndQueuedCandidates()
    {
        var failedCandidate = FailedCandidate("batch-exec-1");
        var queuedCandidate = QueuedCandidate("batch-exec-2");

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { failedCandidate });
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { queuedCandidate });

        _sagas.GetAsync("batch-exec-1", Arg.Any<CancellationToken>())
            .Returns(Saga("batch-exec-1", BatchState.Failed));
        _sagas.GetAsync("batch-exec-2", Arg.Any<CancellationToken>())
            .Returns(Saga("batch-exec-2", BatchState.Queued));

        SetupDeleterSuccess();

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = false, MaxDeletesPerRun = 200 });

        var report = await handler.SweepAsync(false, CancellationToken.None);

        report.DeletedCount.Should().Be(2);
        await _sagas.Received(1).DeleteAsync("batch-exec-1", Arg.Any<CancellationToken>());
        await _sagas.Received(1).DeleteAsync("batch-exec-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_MaxDeletesPerRunCap_LimitsDeletions()
    {
        const int Cap = 1;

        var candidates = new List<RetentionCandidate>
        {
            FailedCandidate("batch-cap-1"),
            FailedCandidate("batch-cap-2"),
            FailedCandidate("batch-cap-3")
        };

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(candidates);
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());

        _sagas.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Saga((string)ci[0], BatchState.Failed));

        SetupDeleterSuccess();

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = false, MaxDeletesPerRun = Cap });

        var report = await handler.SweepAsync(false, CancellationToken.None);

        report.DeletedCount.Should().BeLessOrEqualTo(Cap);
        await _sagas.Received(Cap).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_PartialStoreFailure_SurfacesErrorInReport()
    {
        var candidate = FailedCandidate("batch-fail-1");

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { candidate });
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());

        _sagas.GetAsync("batch-fail-1", Arg.Any<CancellationToken>())
            .Returns(Saga("batch-fail-1", BatchState.Failed));

        _deleter.StoreName.Returns("Cosmos");
        _deleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(new StoreDeletionResult("Cosmos", 0, false, "Cosmos down"));

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = false });

        var report = await handler.SweepAsync(false, CancellationToken.None);

        report.DeletedCount.Should().Be(0);
        report.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SweepAsync_IdempotentReRun_EachRunProducesIndependentReport()
    {
        var candidate = FailedCandidate("batch-idem-1");

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { candidate });
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());

        _sagas.GetAsync("batch-idem-1", Arg.Any<CancellationToken>())
            .Returns(Saga("batch-idem-1", BatchState.Failed));

        SetupDeleterSuccess();

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = false, MaxDeletesPerRun = 200 });

        var report1 = await handler.SweepAsync(false, CancellationToken.None);
        var report2 = await handler.SweepAsync(false, CancellationToken.None);

        report1.DeletedCount.Should().Be(1);
        report2.DeletedCount.Should().Be(1);
        report1.StartedUtc.Should().NotBe(report2.StartedUtc);
    }

    [Fact]
    public async Task SweepAsync_DryRunOverrideTrue_OverridesOptionsDryRunFalse()
    {
        var candidate = FailedCandidate("batch-override-1");

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { candidate });
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());

        var handler = BuildHandler(new RetentionPolicyOptions { DryRun = false });

        var report = await handler.SweepAsync(dryRunOverride: true, CancellationToken.None);

        report.DryRun.Should().BeTrue();
        report.DeletedCount.Should().Be(0);
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_ForceWedgedRunning_CollectsAndDeletesInProgressCandidates()
    {
        var inProgressId = "batch-wedged-1";
        var wedgedCandidate = InProgressCandidate(inProgressId);

        _query.FindFailedOlderThanAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());
        _query.FindStuckIncompleteAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate>());
        _query.FindStuckInProgressAsync(Arg.Any<DateTimeOffset>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<RetentionCandidate> { wedgedCandidate });

        _sagas.GetAsync(inProgressId, Arg.Any<CancellationToken>())
            .Returns(Saga(inProgressId, BatchState.InProgress));

        SetupDeleterSuccess();

        var handler = BuildHandler(new RetentionPolicyOptions
        {
            DryRun = false,
            ForceWedgedRunning = true,
            IncompleteStuckHours = WedgedHours
        });

        var report = await handler.SweepAsync(false, CancellationToken.None);

        report.Candidates.Should().Contain(c => c.BatchId == inProgressId);
        report.DeletedCount.Should().Be(1);
        await _sagas.Received(1).DeleteAsync(inProgressId, Arg.Any<CancellationToken>());
    }
}
