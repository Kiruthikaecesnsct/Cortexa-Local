using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class CancelBatchHandlerTests
{
    private readonly ISagaRepository _sagas;
    private readonly CancelBatchHandler _handler;

    public CancelBatchHandlerTests()
    {
        _sagas = Substitute.For<ISagaRepository>();
        _handler = new CancelBatchHandler(_sagas);
    }

    private static BatchSaga BuildSaga(string batchId, BatchState state, List<DocumentProgress>? documents = null)
    {
        return new BatchSaga(
            batchId,
            state,
            documents ?? [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            activeDocumentIds: new HashSet<string>(),
            queuedDocumentIds: new Queue<string>());
    }

    [Fact]
    public async Task HandleAsync_BatchNotFound_ThrowsBatchNotFoundException()
    {
        _sagas.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        var act = async () => await _handler.HandleAsync("missing", null, CancellationToken.None);

        await act.Should().ThrowAsync<BatchNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_InProgressBatch_MarksCancelledAndPersists()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = BuildSaga("batch-1", BatchState.InProgress, [doc]);
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var result = await _handler.HandleAsync("batch-1", "test reason", CancellationToken.None);

        saga.State.Should().Be(BatchState.Cancelled);
        saga.FailureReason.Should().Be("test reason");
        saga.CancelledAt.Should().NotBeNull();
        result.Status.Should().Be("Cancelled");
        result.BatchId.Should().Be("batch-1");
        await _sagas.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueuedBatch_MarksCancelledAndPersists()
    {
        var saga = BuildSaga("batch-1", BatchState.Queued);
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var result = await _handler.HandleAsync("batch-1", null, CancellationToken.None);

        saga.State.Should().Be(BatchState.Cancelled);
        result.Status.Should().Be("Cancelled");
        await _sagas.Received(1).UpdateAsync(saga, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    public async Task HandleAsync_AlreadyTerminalBatch_ReturnsCurrentStatusWithoutWrite(BatchState terminalState)
    {
        var saga = BuildSaga("batch-1", terminalState);
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var result = await _handler.HandleAsync("batch-1", null, CancellationToken.None);

        await _sagas.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        result.BatchId.Should().Be("batch-1");
    }

    [Fact]
    public async Task HandleAsync_ConcurrencyConflictOnFirstAttempt_ReloadsAndRetries()
    {
        var firstSaga = BuildSaga("batch-1", BatchState.InProgress);
        var reloadedSaga = BuildSaga("batch-1", BatchState.InProgress);

        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(firstSaga, reloadedSaga);
        _sagas.UpdateAsync(firstSaga, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("batch-1"));

        var result = await _handler.HandleAsync("batch-1", "reason", CancellationToken.None);

        result.Status.Should().Be("Cancelled");
        await _sagas.Received(2).GetAsync("batch-1", Arg.Any<CancellationToken>());
        await _sagas.Received(1).UpdateAsync(reloadedSaga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ConcurrencyConflictReloadedSagaIsTerminal_ReturnsCurrentStatusWithNoSecondWrite()
    {
        var firstSaga = BuildSaga("batch-1", BatchState.InProgress);
        var reloadedSaga = BuildSaga("batch-1", BatchState.Completed);

        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(firstSaga, reloadedSaga);
        _sagas.UpdateAsync(firstSaga, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("batch-1"));

        var result = await _handler.HandleAsync("batch-1", null, CancellationToken.None);

        result.Status.Should().Be("Completed");
        await _sagas.Received(1).UpdateAsync(firstSaga, Arg.Any<CancellationToken>());
        await _sagas.DidNotReceive().UpdateAsync(reloadedSaga, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ConcurrencyConflictOnBothAttempts_PropagatesException()
    {
        var firstSaga = BuildSaga("batch-1", BatchState.InProgress);
        var reloadedSaga = BuildSaga("batch-1", BatchState.InProgress);

        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(firstSaga, reloadedSaga);
        _sagas.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("batch-1"));

        var act = async () => await _handler.HandleAsync("batch-1", null, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task HandleAsync_Cancel_SetsNonTerminalDocsToCancelled_LeavesTerminalDocsUnchanged()
    {
        var active = new DocumentProgress("doc-a", DocumentState.Ingested);
        var completed = new DocumentProgress("doc-b", DocumentState.Complete);
        var failed = new DocumentProgress("doc-c", DocumentState.Failed);
        var saga = BuildSaga("batch-1", BatchState.InProgress, [active, completed, failed]);
        saga.ActiveDocumentIds.Add("doc-a");

        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        await _handler.HandleAsync("batch-1", null, CancellationToken.None);

        active.State.Should().Be(DocumentState.Cancelled);
        completed.State.Should().Be(DocumentState.Complete);
        failed.State.Should().Be(DocumentState.Failed);
        saga.ActiveDocumentIds.Should().BeEmpty();
        saga.QueuedDocumentIds.Should().BeEmpty();
    }
}
