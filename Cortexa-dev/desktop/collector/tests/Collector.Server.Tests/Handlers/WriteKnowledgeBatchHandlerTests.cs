using Collector.Server.Application.Building;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Events;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;
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
    private readonly FakePipelineRowStore _store = new();
    private readonly FakeIngestionEventPublisher _publisher = new();
    private readonly CapturingLogger<BatchRollback> _rollbackLog = new();

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

    [Theory]
    [InlineData(FakePipelineRowStore.DocumentsStage, new[] { "documents" })]
    [InlineData(FakePipelineRowStore.ChunksStage, new[] { "chunks", "documents" })]
    [InlineData(FakePipelineRowStore.ProvenanceStage, new[] { "provenance", "chunks", "documents" })]
    [InlineData(FakePipelineRowStore.SagaStage, new[] { "saga", "provenance", "chunks", "documents" })]
    public async Task Handle_WriteStageFails_DeletesStartedStagesInReverseOrderAndLeavesNoRows(string failingStage, string[] expectedDeletes)
    {
        _store.FailAtStage = failingStage;
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Equal(expectedDeletes, _store.DeleteCalls);
        Assert.Equal(0, _store.RowCount);
    }

    [Fact]
    public async Task Handle_ChunkStageFails_DeletesEveryPlannedChunkAndDocumentId()
    {
        var command = TwoDocumentCommand();
        var plan = BatchPlanBuilder.Build(command, TestData.FixedTime);
        _store.FailAtStage = FakePipelineRowStore.ChunksStage;
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(plan.Chunks.Select(row => row.Id), _store.DeletedIds["chunks"]);
        Assert.Equal(plan.Documents.Select(row => row.Id), _store.DeletedIds["documents"]);
    }

    [Fact]
    public async Task Handle_SagaAlreadyExists_DeletesNothing()
    {
        _store.ConflictWithoutSaga = true;
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<SagaAlreadyExistsException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Empty(_store.DeleteCalls);
        Assert.NotEmpty(_store.Documents);
    }

    [Fact]
    public async Task Handle_PreSagaFailureWhileSagaExists_SkipsRollback()
    {
        _store.Sagas[TestData.BatchId] = TestSagas.Existing(TestData.BatchId, TestData.PaperDocumentId);
        _store.FailAtStage = FakePipelineRowStore.ChunksStage;
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Empty(_store.DeleteCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Handle_PublishFailsAfterEvents_DeletesNothing(int publishedBeforeFailure)
    {
        _publisher.FailAfter = publishedBeforeFailure;
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Empty(_store.DeleteCalls);
        Assert.Equal(publishedBeforeFailure, _publisher.Published.Count);
        Assert.Single(_store.Sagas);
        Assert.Equal(2, _store.Documents.Count);
    }

    [Fact]
    public async Task Handle_SagaDeleteFails_StopsRollbackAndKeepsRows()
    {
        _store.FailAtStage = FakePipelineRowStore.SagaStage;
        _store.Sagas[TestData.BatchId] = TestSagas.Existing(TestData.BatchId, TestData.PaperDocumentId);
        _store.FailDeleteIds.Add(TestData.BatchId);
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Equal(["saga"], _store.DeleteCalls);
        Assert.Equal(2, _store.Documents.Count);
        Assert.Equal(2, _store.Chunks.Count);
        Assert.Equal(2, _store.Provenance.Count);
    }

    [Fact]
    public async Task Handle_PerIdDeleteFails_LogsIdAndDeletesOthers()
    {
        var plan = BatchPlanBuilder.Build(TwoDocumentCommand(), TestData.FixedTime);
        var stuckChunkId = plan.Chunks[0].Id;
        _store.FailAtStage = FakePipelineRowStore.ProvenanceStage;
        _store.FailDeleteIds.Add(stuckChunkId);
        var handler = CreateStoreHandler();

        await Assert.ThrowsAsync<PipelineWriteException>(() => handler.HandleAsync(TwoDocumentCommand(), TestContext.Current.CancellationToken));

        Assert.Contains(_rollbackLog.Messages, message => message.Contains(stuckChunkId));
        Assert.Equal([stuckChunkId], _store.Chunks.Select(row => row.Id));
        Assert.Empty(_store.Documents);
        Assert.Equal([stuckChunkId], _store.Provenance.Select(row => row.Id));
    }

    [Fact]
    public async Task Handle_RequestCancelled_StillRollsBackWithItsOwnToken()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = CreateStoreHandler();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(TwoDocumentCommand(), cancelled.Token));

        Assert.Equal(["documents"], _store.DeleteCalls);
        Assert.False(_store.AnyDeleteSawCancelledToken);
        Assert.Equal(0, _store.RowCount);
    }

    private static WriteKnowledgeBatchCommand TwoDocumentCommand() =>
        TestData.Command(TestData.PaperDocument(), TestData.CodeDocument());

    private WriteKnowledgeBatchHandler CreateStoreHandler() =>
        new(
            _store,
            new IngestionEventDispatcher(_publisher, new FixedClock(), NullLogger<IngestionEventDispatcher>.Instance),
            new BatchRollback(_store, _rollbackLog),
            new FixedClock(),
            NullLogger<WriteKnowledgeBatchHandler>.Instance);

    private static WriteKnowledgeBatchHandler CreateHandler(IPipelineRowStore store, IIngestionEventPublisher publisher) =>
        new(
            store,
            new IngestionEventDispatcher(publisher, new FixedClock(), NullLogger<IngestionEventDispatcher>.Instance),
            new BatchRollback(store, NullLogger<BatchRollback>.Instance),
            new FixedClock(),
            NullLogger<WriteKnowledgeBatchHandler>.Instance);

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

        public Task<IReadOnlyList<string>> DeleteSagaAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> DeleteProvenanceAsync(
            string batchId,
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> DeleteChunksAsync(
            string batchId,
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> DeleteDocumentsAsync(
            string batchId,
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<DocumentRow>> GetDocumentsByBatchAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentRow>>([]);

        public Task<SagaRow?> GetSagaAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<SagaRow?>(null);

        public Task<IReadOnlyList<SagaRow>> ListSagasByOwnerAsync(
            string ownerUserId,
            string orgId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SagaRow>>([]);

        public Task<BatchResultRows> GetResultsByBatchAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult(new BatchResultRows([], null));

        public Task<IReadOnlyList<VerdictSummaryRow>> GetVerdictSummariesByBatchAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VerdictSummaryRow>>([]);

        public Task<IReadOnlyList<EvidenceCountRow>> GetEvidenceCountsByBatchAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvidenceCountRow>>([]);

        public Task<IReadOnlyList<ChunkKnowledgeRow>> GetChunkKnowledgeByBatchAsync(string batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChunkKnowledgeRow>>([]);
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
