using Collector.Domain.Enums;
using Collector.Domain.Upload;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;
using Collector.Server.Tests.Fakes;
using Collector.Server.Tests.Upload;

namespace Collector.Server.Tests.Handlers;

public class SubmitKnowledgeUploadHandlerTests
{
    private const string OtherOrgId = "org-other";
    private const int QueuedDocumentCount = 2;

    private readonly UploadPipeline _pipeline = new();

    [Fact]
    public async Task HandleAsync_NewUpload_ReturnsCreatedWithDeterministicIds()
    {
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());
        var expectedBatchId = DeterministicIds.BatchId(TestIdentity.UserId, TestIdentity.IdempotencyKey);

        var outcome = await _pipeline.SubmitAsync(request);

        Assert.False(outcome.IsReplay);
        Assert.Equal(expectedBatchId, outcome.Result!.BatchId);
        Assert.Equal(
            [
                DeterministicIds.DocumentId(expectedBatchId, UploadRequests.ClientId(1)),
                DeterministicIds.DocumentId(expectedBatchId, UploadRequests.ClientId(2))
            ],
            outcome.Result.DocumentIds);
    }

    [Fact]
    public async Task HandleAsync_SameKeyTwice_SecondCallReplaysWithoutWritingAndRepublishesQueuedDocuments()
    {
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());
        var first = await _pipeline.SubmitAsync(request);
        var writesAfterFirst = _pipeline.Store.WriteCalls;
        var publishesAfterFirst = _pipeline.Publisher.Published.Count;

        var second = await _pipeline.SubmitAsync(request);

        Assert.True(second.IsReplay);
        Assert.Equal(first.Result!.BatchId, second.Result!.BatchId);
        Assert.Equal(first.Result.DocumentIds, second.Result.DocumentIds);
        Assert.Equal(writesAfterFirst, _pipeline.Store.WriteCalls);
        Assert.Equal(publishesAfterFirst + QueuedDocumentCount, _pipeline.Publisher.Published.Count);
    }

    [Fact]
    public async Task HandleAsync_ExistingSaga_ReplaysWithoutReadingModelsOrWritingAndRepublishes()
    {
        SeedSaga(TestSagas.Existing(BatchId(), "doc-a", "doc-b"));

        var outcome = await _pipeline.SubmitAsync(UploadRequests.Valid());

        Assert.True(outcome.IsReplay);
        Assert.Equal(["doc-a", "doc-b"], outcome.Result!.DocumentIds);
        Assert.Equal(0, _pipeline.Store.WriteCalls);
        Assert.Equal(0, _pipeline.Models.CallCount);
        Assert.Equal(["doc-a", "doc-b"], _pipeline.Publisher.Published.Select(envelope => envelope.DocumentId));
    }

    [Fact]
    public async Task HandleAsync_SameKeyDifferentBody_ReturnsConflictAndTouchesNothing()
    {
        await _pipeline.SubmitAsync(UploadRequests.Valid());
        var writesAfterFirst = _pipeline.Store.WriteCalls;
        var publishesAfterFirst = _pipeline.Publisher.Published.Count;
        var different = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());

        var outcome = await _pipeline.SubmitAsync(different);

        Assert.True(outcome.IsConflict);
        Assert.Equal(writesAfterFirst, _pipeline.Store.WriteCalls);
        Assert.Equal(publishesAfterFirst, _pipeline.Publisher.Published.Count);
    }

    [Fact]
    public async Task HandleAsync_StoredFingerprintMissing_ReplaysEvenWithDifferentBody()
    {
        var first = await _pipeline.SubmitAsync(UploadRequests.Valid());
        var batchId = first.Result!.BatchId;
        _pipeline.Store.Sagas[batchId] = _pipeline.Store.Sagas[batchId] with { RequestFingerprint = null };
        var different = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());

        var outcome = await _pipeline.SubmitAsync(different);

        Assert.True(outcome.IsReplay);
    }

    [Fact]
    public async Task HandleAsync_ReplayWithMixedStates_RepublishesOnlyQueuedDocuments()
    {
        SeedSaga(TestSagas.WithDocumentStates(
            BatchId(),
            wantsHarvesting: true,
            wantsSeeding: true,
            RowConstants.SagaDocumentStateQueued,
            RowConstants.SagaDocumentStateIngested,
            RowConstants.SagaDocumentStateQueued));

        await _pipeline.SubmitAsync(UploadRequests.Valid());

        Assert.Equal(["doc-0", "doc-2"], _pipeline.Publisher.Published.Select(envelope => envelope.DocumentId));
    }

    [Fact]
    public async Task HandleAsync_ReplayWhenAllDocumentsIngested_PublishesNothing()
    {
        SeedSaga(TestSagas.WithDocumentStates(
            BatchId(),
            wantsHarvesting: true,
            wantsSeeding: true,
            RowConstants.SagaDocumentStateIngested,
            RowConstants.SagaDocumentStateIngested));

        var outcome = await _pipeline.SubmitAsync(UploadRequests.Valid());

        Assert.True(outcome.IsReplay);
        Assert.Empty(_pipeline.Publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_QueuedDocumentWithoutRow_IsNotRepublished()
    {
        var batchId = BatchId();
        _pipeline.Store.Sagas[batchId] = TestSagas.Existing(batchId, "doc-a", "doc-b");
        _pipeline.Store.Documents.Add(TestSagas.DocumentFor(batchId, "doc-a"));

        await _pipeline.SubmitAsync(UploadRequests.Valid());

        Assert.Equal(["doc-a"], _pipeline.Publisher.Published.Select(envelope => envelope.DocumentId));
    }

    [Fact]
    public async Task HandleAsync_PublishFailsThenSameKeyReplays_PublishesAndReturnsReplayed()
    {
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());
        _pipeline.Publisher.FailAfter = 0;
        await Assert.ThrowsAsync<PipelineWriteException>(() => _pipeline.SubmitAsync(request));
        _pipeline.Publisher.FailAfter = null;

        var outcome = await _pipeline.SubmitAsync(request);

        Assert.True(outcome.IsReplay);
        Assert.Equal(QueuedDocumentCount, _pipeline.Publisher.Published.Count);
    }

    [Fact]
    public async Task HandleAsync_SagaCreatedByConcurrentRequest_ReplaysWinnerAndPublishesNothing()
    {
        var batchId = DeterministicIds.BatchId(TestIdentity.UserId, TestIdentity.IdempotencyKey);
        _pipeline.Store.RaceWinner = TestSagas.Existing(batchId, "winner-doc");

        var outcome = await _pipeline.SubmitAsync(UploadRequests.Valid());

        Assert.True(outcome.IsReplay);
        Assert.Equal(["winner-doc"], outcome.Result!.DocumentIds);
        Assert.Empty(_pipeline.Publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_SagaConflictButNoSagaOnReread_Throws()
    {
        _pipeline.Store.ConflictWithoutSaga = true;

        var exception = await Assert.ThrowsAsync<SagaAlreadyExistsException>(() => _pipeline.SubmitAsync(UploadRequests.Valid()));

        Assert.Equal(DeterministicIds.BatchId(TestIdentity.UserId, TestIdentity.IdempotencyKey), exception.BatchId);
    }

    [Fact]
    public async Task HandleAsync_NewUpload_SagaCarriesEngineAndConfiguredModels()
    {
        var outcome = await _pipeline.SubmitAsync(UploadRequests.Valid());

        var saga = _pipeline.Store.Sagas[outcome.Result!.BatchId];
        Assert.Equal(UploadTestOptions.ConfiguredEngine.ToLowerInvariant(), saga.Engine);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.ExtractionModel, saga.ExtractionModel);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.PrimaryEvidenceModel, saga.PrimaryEvidenceModel);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.ScoringModel, saga.ScoringModel);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.SeedingModel, saga.SeedingModel);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.SeedingMode, saga.SeedingMode);
    }

    [Fact]
    public async Task HandleAsync_NewUpload_SagaOrgAndOwnerComeFromCaller()
    {
        var caller = new UploadCaller(TestIdentity.OtherUserId, OtherOrgId);

        var outcome = await _pipeline.SubmitAsync(UploadRequests.Valid(), caller);

        var saga = _pipeline.Store.Sagas[outcome.Result!.BatchId];
        Assert.Equal(OtherOrgId, saga.OrgId);
        Assert.Equal(TestIdentity.OtherUserId, saga.OwnerUserId);
    }

    [Fact]
    public async Task HandleAsync_SameKeyDifferentUsers_CreatesSeparateBatches()
    {
        var first = await _pipeline.SubmitAsync(UploadRequests.Valid());
        var second = await _pipeline.SubmitAsync(UploadRequests.Valid(), new UploadCaller(TestIdentity.OtherUserId, TestIdentity.OrgId));

        Assert.False(second.IsReplay);
        Assert.NotEqual(first.Result!.BatchId, second.Result!.BatchId);
    }

    [Fact]
    public async Task HandleAsync_DocumentWithZeroItems_IsDroppedFromRowsAndSaga()
    {
        var empty = UploadRequests.Document(2, SourceKind.Code, "empty.py");
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), empty);

        var outcome = await _pipeline.SubmitAsync(request);

        var saga = _pipeline.Store.Sagas[outcome.Result!.BatchId];
        Assert.Single(outcome.Result.DocumentIds);
        Assert.Single(_pipeline.Store.Documents);
        Assert.Equal(1, saga.TotalDocumentCount);
    }

    [Fact]
    public async Task HandleAsync_BatchNameAndFilename_AreTrimmedAndSanitized()
    {
        var document = UploadRequests.PaperDocument() with { Filename = "pa\u202Eper\".pdf" };
        var request = UploadRequests.Valid(document) with { BatchName = "  Padded Name  " };

        var outcome = await _pipeline.SubmitAsync(request);

        Assert.Equal("Padded Name", _pipeline.Store.Sagas[outcome.Result!.BatchId].BatchName);
        Assert.Equal("paper.pdf", Assert.Single(_pipeline.Store.Documents).Filename);
    }

    [Fact]
    public async Task HandleAsync_InvalidRequest_ReturnsErrorsAndWritesNothing()
    {
        var item = UploadRequests.Item(KnowledgeKind.Logic, UnitKind.File);

        var outcome = await _pipeline.SubmitAsync(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.True(outcome.IsInvalid);
        Assert.Contains(outcome.Errors, error => error.Field == "documents[0].knowledge_items[0].unit_kind");
        Assert.Equal(0, _pipeline.Store.WriteCalls);
        Assert.Empty(_pipeline.Publisher.Published);
    }

    private static string BatchId() => DeterministicIds.BatchId(TestIdentity.UserId, TestIdentity.IdempotencyKey);

    private void SeedSaga(SagaRow saga)
    {
        _pipeline.Store.Sagas[saga.BatchId] = saga;
        _pipeline.Store.Documents.AddRange(
            saga.Documents.Select(document => TestSagas.DocumentFor(saga.BatchId, document.DocumentId)));
    }
}
