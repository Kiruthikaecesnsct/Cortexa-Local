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

public sealed class RetryDocumentHandlerTests
{
    private readonly ISagaRepository _sagas;
    private readonly IDocumentRepository _documents;
    private readonly IEventPublisher _publisher;
    private readonly RetryDocumentHandler _handler;

    public RetryDocumentHandlerTests()
    {
        _sagas = Substitute.For<ISagaRepository>();
        _documents = Substitute.For<IDocumentRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _handler = new RetryDocumentHandler(_sagas, _documents, _publisher);
    }

    private static BatchSaga BuildSagaWithFailedDoc(string batchId, string docId)
    {
        var doc = new DocumentProgress(docId, DocumentState.Failed) { FailureReason = "Extraction failed." };
        return new BatchSaga(batchId, BatchState.Failed, [doc], false, false, 1, "etag-1", 1);
    }

    [Fact]
    public async Task HandleAsync_BatchNotFound_ThrowsBatchNotFoundException()
    {
        _sagas.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        var act = async () => await _handler.HandleAsync("missing", "doc-1", CancellationToken.None);

        await act.Should().ThrowAsync<BatchNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_DocumentNotInSaga_ThrowsDocumentNotFoundException()
    {
        const string batchId = "batch-1";
        var saga = new BatchSaga(batchId, BatchState.InProgress, [], false, false, 1, null, 1);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(saga);

        var act = async () => await _handler.HandleAsync(batchId, "ghost-doc", CancellationToken.None);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_DocumentNotInFailedState_ThrowsInvalidOperationException()
    {
        const string batchId = "batch-1";
        var doc = new DocumentProgress("doc-1", DocumentState.Ingested);
        var saga = new BatchSaga(batchId, BatchState.InProgress, [doc], false, false, 1, null, 1);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(saga);

        var act = async () => await _handler.HandleAsync(batchId, "doc-1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed state*");
    }

    [Fact]
    public async Task HandleAsync_FailedDocument_PublishesIngestionRequestedEvent()
    {
        const string batchId = "batch-1";
        const string docId = "doc-1";
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(BuildSagaWithFailedDoc(batchId, docId));

        await _handler.HandleAsync(batchId, docId, CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e =>
                e.EventType == SagaEventType.IngestionRequested &&
                e.BatchId == batchId &&
                e.DocumentId == docId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FailedDocument_ResetsDocumentStateToQueued()
    {
        const string batchId = "batch-1";
        const string docId = "doc-1";
        var saga = BuildSagaWithFailedDoc(batchId, docId);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(saga);

        await _handler.HandleAsync(batchId, docId, CancellationToken.None);

        var doc = saga.FindDocument(docId);
        doc.Should().NotBeNull();
        doc!.State.Should().Be(DocumentState.Queued);
        doc.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_FailedDocument_UpdatesDocumentStatusInRepository()
    {
        const string batchId = "batch-1";
        const string docId = "doc-1";
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(BuildSagaWithFailedDoc(batchId, docId));

        await _handler.HandleAsync(batchId, docId, CancellationToken.None);

        await _documents.Received(1).UpdateStatusAsync(batchId, docId, "queued", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FailedBatch_TransitionsBatchToInProgress()
    {
        const string batchId = "batch-1";
        const string docId = "doc-1";
        var saga = BuildSagaWithFailedDoc(batchId, docId);
        saga.State.Should().Be(BatchState.Failed);
        _sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns(saga);

        await _handler.HandleAsync(batchId, docId, CancellationToken.None);

        saga.State.Should().Be(BatchState.InProgress);
    }
}
