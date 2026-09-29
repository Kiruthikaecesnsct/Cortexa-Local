using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class CreateBatchFanOutHandlerTests
{
    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly CreateBatchFanOutHandler _handler;

    private const int DefaultCap = 3;

    public CreateBatchFanOutHandlerTests()
    {
        _repository = Substitute.For<ISagaRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _handler = BuildHandler(DefaultCap);
    }

    private CreateBatchFanOutHandler BuildHandler(int cap)
    {
        var settings = Options.Create(new OrchestratorSettings { ConcurrencyCap = cap });
        return new CreateBatchFanOutHandler(_repository, _publisher, settings);
    }

    private static EventEnvelope BuildEnvelope(string batchId, IEnumerable<string> docIds)
    {
        var payload = new Dictionary<string, object>
        {
            ["document_ids"] = JsonSerializer.SerializeToElement(docIds)
        };
        return new EventEnvelope { EventType = "batch.created", BatchId = batchId, Payload = payload };
    }

    private static List<string> DocIds(int count) =>
        Enumerable.Range(1, count).Select(i => $"doc-{i}").ToList();

    [Fact]
    public async Task HandleAsync_NewBatch_PublishesExactlyCapIngestionRequestedEvents()
    {
        const int DocCount = 10;
        const int Cap = 3;
        var handler = BuildHandler(Cap);
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        await handler.HandleAsync(BuildEnvelope("batch-1", DocIds(DocCount)), CancellationToken.None);

        await _publisher.Received(Cap).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.IngestionRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NewBatch_SagaCreatedWithAllDocumentIds()
    {
        const int DocCount = 10;
        const int Cap = 3;
        var handler = BuildHandler(Cap);
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        BatchSaga? capturedSaga = null;
        await _repository.CreateAsync(
            Arg.Do<BatchSaga>(s => capturedSaga = s),
            Arg.Any<CancellationToken>());

        await handler.HandleAsync(BuildEnvelope("batch-1", DocIds(DocCount)), CancellationToken.None);

        capturedSaga.Should().NotBeNull();
        capturedSaga!.Documents.Count.Should().Be(Cap); // only active (capped) docs seeded into Documents immediately
        capturedSaga.ActiveDocumentIds.Count.Should().Be(Cap);
    }

    [Fact]
    public async Task HandleAsync_ExistingBatchAlreadySeeded_ReturnsAlreadyAppliedAndDoesNotPublish()
    {
        var existingActiveIds = new HashSet<string> { "doc-1" };
        var existingSaga = new BatchSaga(
            "batch-1",
            BatchState.InProgress,
            [new DocumentProgress("doc-1", DocumentState.Queued)],
            false, false, 1, null, 1,
            activeDocumentIds: existingActiveIds);

        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(existingSaga);

        var result = await _handler.HandleAsync(BuildEnvelope("batch-1", DocIds(5)), CancellationToken.None);

        result.Should().Be(HandlerResult.AlreadyApplied);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PayloadMissingDocumentIdsField_ThrowsPermanentProcessingException()
    {
        var envelope = new EventEnvelope
        {
            EventType = "batch.created",
            BatchId = "batch-1",
            Payload = []
        };

        var act = async () => await _handler.HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>()
            .WithMessage("*document_ids*");
    }

    [Fact]
    public async Task HandleAsync_EmptyDocumentIds_ThrowsPermanentProcessingException()
    {
        var act = async () => await _handler.HandleAsync(BuildEnvelope("batch-1", []), CancellationToken.None);

        await act.Should().ThrowAsync<PermanentProcessingException>();
    }

    [Fact]
    public async Task HandleAsync_CapLargerThanDocCount_PublishesAllDocs()
    {
        const int DocCount = 3;
        const int Cap = 10;
        var handler = BuildHandler(Cap);
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        await handler.HandleAsync(BuildEnvelope("batch-1", DocIds(DocCount)), CancellationToken.None);

        await _publisher.Received(DocCount).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.IngestionRequested),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CapOfOne_PublishesExactlyOneIngestionRequest()
    {
        const int DocCount = 5;
        const int Cap = 1;
        var handler = BuildHandler(Cap);
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);

        await handler.HandleAsync(BuildEnvelope("batch-1", DocIds(DocCount)), CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.IngestionRequested),
            Arg.Any<CancellationToken>());
    }
}
