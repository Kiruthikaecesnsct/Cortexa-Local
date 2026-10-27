using Collector.Server.Application.Handlers;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;
using Collector.Server.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Server.Tests.Handlers;

public class RetryBatchHandlerTests
{
    private const string BatchId = "batch-mine";

    [Fact]
    public async Task HandleAsync_FailedBatch_ResetsAllDocumentsToQueuedAndDispatches()
    {
        var store = new FakePipelineRowStore();
        var saga = TestSagas.Existing(BatchId, "doc-1", "doc-2");
        saga = TestSagas.WithDocumentState(saga, "doc-1", RowConstants.SagaDocumentStateComplete);
        saga = TestSagas.WithDocumentState(saga, "doc-2", RowConstants.SagaDocumentStateFailed);
        store.Sagas[BatchId] = saga;
        store.Documents.AddRange([TestSagas.DocumentFor(BatchId, "doc-1"), TestSagas.DocumentFor(BatchId, "doc-2")]);
        var publisher = new FakeIngestionEventPublisher();
        var handler = CreateHandler(store, publisher);

        var outcome = await handler.HandleAsync(BatchId, TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.True(outcome.Found);
        Assert.Equal(["doc-1", "doc-2"], outcome.DocumentIds);
        var resetSaga = store.Sagas[BatchId];
        Assert.All(resetSaga.Documents, document => Assert.Equal(RowConstants.SagaDocumentStateQueued, document.State));
        Assert.Equal(RowConstants.SagaStateInProgress, resetSaga.State);
        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(["doc-1", "doc-2"], publisher.Published.Select(envelope => envelope.DocumentId));
    }

    [Fact]
    public async Task HandleAsync_UnknownBatch_ReturnsNotFoundAndDoesNotDispatch()
    {
        var store = new FakePipelineRowStore();
        var publisher = new FakeIngestionEventPublisher();
        var handler = CreateHandler(store, publisher);

        var outcome = await handler.HandleAsync("does-not-exist", TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.False(outcome.Found);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_NonOwningCaller_ReturnsNotFoundAndDoesNotDispatch()
    {
        var store = new FakePipelineRowStore();
        var saga = TestSagas.Existing(BatchId, "doc-1");
        saga = TestSagas.WithDocumentState(saga, "doc-1", RowConstants.SagaDocumentStateFailed);
        store.Sagas[BatchId] = saga;
        store.Documents.Add(TestSagas.DocumentFor(BatchId, "doc-1"));
        var publisher = new FakeIngestionEventPublisher();
        var handler = CreateHandler(store, publisher);
        var otherCaller = new UploadCaller(TestIdentity.OtherUserId, TestIdentity.OrgId);

        var outcome = await handler.HandleAsync(BatchId, otherCaller, TestContext.Current.CancellationToken);

        Assert.False(outcome.Found);
        Assert.Empty(publisher.Published);
        Assert.Equal(RowConstants.SagaDocumentStateFailed, store.Sagas[BatchId].Documents[0].State);
    }

    [Fact]
    public async Task HandleAsync_MissingDocumentRow_SkipsItButDispatchesTheRest()
    {
        var store = new FakePipelineRowStore();
        var saga = TestSagas.Existing(BatchId, "doc-1", "doc-2");
        saga = TestSagas.WithDocumentState(saga, "doc-1", RowConstants.SagaDocumentStateFailed);
        saga = TestSagas.WithDocumentState(saga, "doc-2", RowConstants.SagaDocumentStateFailed);
        store.Sagas[BatchId] = saga;
        store.Documents.Add(TestSagas.DocumentFor(BatchId, "doc-1"));
        var publisher = new FakeIngestionEventPublisher();
        var handler = CreateHandler(store, publisher);

        var outcome = await handler.HandleAsync(BatchId, TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.True(outcome.Found);
        Assert.Equal(["doc-1"], outcome.DocumentIds);
        Assert.Equal("doc-1", Assert.Single(publisher.Published).DocumentId);
    }

    private static RetryBatchHandler CreateHandler(FakePipelineRowStore store, FakeIngestionEventPublisher publisher) =>
        new(
            store,
            new IngestionEventDispatcher(publisher, new FixedClock(), NullLogger<IngestionEventDispatcher>.Instance),
            NullLogger<RetryBatchHandler>.Instance);
}
