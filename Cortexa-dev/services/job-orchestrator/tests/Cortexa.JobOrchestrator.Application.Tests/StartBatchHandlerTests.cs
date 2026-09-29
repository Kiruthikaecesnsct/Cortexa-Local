using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class StartBatchHandlerTests
{
    private readonly ISagaRepository _sagas;
    private readonly IDocumentRepository _documents;
    private readonly IEventPublisher _publisher;
    private readonly StartBatchHandler _handler;

    public StartBatchHandlerTests()
    {
        _sagas = Substitute.For<ISagaRepository>();
        _documents = Substitute.For<IDocumentRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _handler = new StartBatchHandler(_sagas, _documents, _publisher);
    }

    private static BatchSaga BuildQueuedSaga(string batchId) =>
        new(batchId, BatchState.Queued, [], false, false, 0, null, 1);

    private static IReadOnlyList<DocumentRecord> BuildDocRecords(string batchId, int count) =>
        Enumerable.Range(0, count)
            .Select(i => new DocumentRecord($"doc-{i}", batchId, $"file{i}.pdf", "https://blob/file"))
            .ToList();

    [Fact]
    public async Task HandleAsync_BatchNotFound_ThrowsBatchNotFoundException()
    {
        _sagas.GetAsync("missing-id", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        var act = async () => await _handler.HandleAsync("missing-id", CancellationToken.None);

        await act.Should().ThrowAsync<BatchNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_QueuedBatch_PublishesBatchCreatedEvent()
    {
        const string batchId = "batch-1";
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(BuildQueuedSaga(batchId));
        _documents.ListByBatchAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildDocRecords(batchId, 3));

        await _handler.HandleAsync(batchId, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.BatchCreated && e.BatchId == batchId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueuedBatch_PublishesDocumentIdsInPayload()
    {
        const string batchId = "batch-1";
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(BuildQueuedSaga(batchId));
        _documents.ListByBatchAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildDocRecords(batchId, 2));

        EventEnvelope? capturedEnvelope = null;
        await _publisher.PublishAsync(
            Arg.Do<EventEnvelope>(e => capturedEnvelope = e),
            Arg.Any<CancellationToken>());

        await _handler.HandleAsync(batchId, CancellationToken.None);

        capturedEnvelope.Should().NotBeNull();
        capturedEnvelope!.Payload.Should().ContainKey("document_ids");
        var ids = capturedEnvelope.Payload["document_ids"] as string[];
        ids.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_BatchAlreadyInProgress_DoesNotPublishEvent()
    {
        const string batchId = "batch-1";
        var inProgressSaga = new BatchSaga(batchId, BatchState.InProgress, [], false, false, 1, null, 1);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(inProgressSaga);

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Status.Should().Be("started");
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BatchAlreadyCompleted_DoesNotPublishEvent()
    {
        const string batchId = "batch-1";
        var completedSaga = new BatchSaga(batchId, BatchState.Completed, [], false, false, 1, null, 1);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(completedSaga);

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Status.Should().Be("started");
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueuedBatchWithNoDocuments_ThrowsInvalidOperationException()
    {
        const string batchId = "batch-1";
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(BuildQueuedSaga(batchId));
        _documents.ListByBatchAsync(batchId, Arg.Any<CancellationToken>()).Returns([]);

        var act = async () => await _handler.HandleAsync(batchId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No documents*");
    }
}
