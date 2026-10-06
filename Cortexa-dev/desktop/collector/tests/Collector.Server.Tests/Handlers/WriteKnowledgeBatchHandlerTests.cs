using Collector.Server.Application.Errors;
using Collector.Server.Application.Events;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Server.Tests.Handlers;

public class WriteKnowledgeBatchHandlerTests
{
    private const string DocumentsCall = "documents";
    private const string ChunksCall = "chunks";
    private const string ProvenanceCall = "provenance";
    private const string SagaCall = "saga";
    private const string PublishCall = "publish";

    private readonly List<string> _calls = [];
    private readonly List<EventEnvelope> _published = [];

    [Fact]
    public async Task Handle_WritesRowsInOrderThenPublishes()
    {
        var handler = CreateHandler(new RecordingStore(_calls), new RecordingPublisher(_calls, _published));

        await handler.HandleAsync(TestData.Command(TestData.PaperDocument(), TestData.CodeDocument()), TestContext.Current.CancellationToken);

        Assert.Equal([DocumentsCall, ChunksCall, ProvenanceCall, SagaCall, PublishCall, PublishCall], _calls);
    }

    [Fact]
    public async Task Handle_PublishesOneIngestionCompletedPerDocumentWithChunkCount()
    {
        var store = new RecordingStore(_calls);
        var handler = CreateHandler(store, new RecordingPublisher(_calls, _published));
        var command = TestData.Command(
            TestData.PaperDocument(TestData.PaperItem(), TestData.PlainItem("B")),
            TestData.CodeDocument());

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, _published.Count);
        Assert.All(_published, envelope => Assert.Equal("ingestion.completed", envelope.EventType));
        Assert.Equal(result.DocumentIds, _published.Select(envelope => envelope.DocumentId));
        foreach (var envelope in _published)
        {
            var chunkRows = store.Chunks.Count(chunk => chunk.DocumentId == envelope.DocumentId);
            Assert.Equal(chunkRows, envelope.Payload.ChunkCount);
        }
    }

    [Fact]
    public async Task Handle_NeverPublishesOtherEventTypes()
    {
        var handler = CreateHandler(new RecordingStore(_calls), new RecordingPublisher(_calls, _published));

        await handler.HandleAsync(TestData.Command(TestData.PaperDocument()), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_published, envelope => envelope.EventType is "ingestion.requested" or "batch.created");
    }

    [Fact]
    public async Task Handle_SagaWriteFails_PublishesNothing()
    {
        var store = new RecordingStore(_calls) { SagaFailure = new SagaAlreadyExistsException(TestData.BatchId) };
        var handler = CreateHandler(store, new RecordingPublisher(_calls, _published));

        await Assert.ThrowsAsync<SagaAlreadyExistsException>(
            () => handler.HandleAsync(TestData.Command(TestData.PaperDocument()), TestContext.Current.CancellationToken));

        Assert.Empty(_published);
        Assert.DoesNotContain(PublishCall, _calls);
    }

    [Fact]
    public async Task Handle_PublishFails_ThrowsWithBatchId()
    {
        var publisher = new RecordingPublisher(_calls, _published) { Failure = new InvalidOperationException("broker down") };
        var handler = CreateHandler(new RecordingStore(_calls), publisher);

        var exception = await Assert.ThrowsAsync<PipelineWriteException>(
            () => handler.HandleAsync(TestData.Command(TestData.PaperDocument()), TestContext.Current.CancellationToken));

        Assert.Equal(TestData.BatchId, exception.BatchId);
    }

    [Fact]
    public async Task Handle_ReturnsBatchAndDocumentIds()
    {
        var handler = CreateHandler(new RecordingStore(_calls), new RecordingPublisher(_calls, _published));

        var result = await handler.HandleAsync(
            TestData.Command(TestData.PaperDocument(), TestData.CodeDocument()),
            TestContext.Current.CancellationToken);

        Assert.Equal(TestData.BatchId, result.BatchId);
        Assert.Equal([TestData.PaperDocumentId, TestData.CodeDocumentId], result.DocumentIds);
    }

    private static WriteKnowledgeBatchHandler CreateHandler(IPipelineRowStore store, IIngestionEventPublisher publisher) =>
        new(store, publisher, new FixedClock(), NullLogger<WriteKnowledgeBatchHandler>.Instance);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => TestData.FixedTime;
    }

    private sealed class RecordingStore(List<string> calls) : IPipelineRowStore
    {
        public List<ChunkRow> Chunks { get; } = [];

        public Exception? SagaFailure { get; init; }

        public Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken)
        {
            calls.Add(DocumentsCall);
            return Task.CompletedTask;
        }

        public Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken)
        {
            calls.Add(ChunksCall);
            Chunks.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken)
        {
            calls.Add(ProvenanceCall);
            return Task.CompletedTask;
        }

        public Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken)
        {
            calls.Add(SagaCall);
            return SagaFailure is null ? Task.CompletedTask : Task.FromException(SagaFailure);
        }

        public Task<SagaRow?> GetSagaAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<SagaRow?>(null);
    }

    private sealed class RecordingPublisher(List<string> calls, List<EventEnvelope> published) : IIngestionEventPublisher
    {
        public Exception? Failure { get; init; }

        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            calls.Add(PublishCall);
            published.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
