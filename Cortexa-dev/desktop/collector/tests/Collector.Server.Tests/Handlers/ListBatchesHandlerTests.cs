using Collector.Server.Application.Handlers;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Handlers;

public class ListBatchesHandlerTests
{
    private const string BatchId = "batch-mine";
    private const string BatchName = "Batch One";
    private const string State = RowConstants.SagaStateInProgress;
    private const int ExtractionCompleted = 2;
    private const int ExtractionTotal = 7;
    private const int EvidenceCompleted = 3;
    private const int EmbeddingCompleted = 4;
    private const int EmbeddingTotal = 9;

    [Fact]
    public async Task HandleAsync_MapsEachStageCountToItsOwnField()
    {
        var store = new FakePipelineRowStore();
        store.Sagas[BatchId] = BuildSaga();

        var handler = new ListBatchesHandler(store);

        var batches = await handler.HandleAsync(TestIdentity.Caller, TestContext.Current.CancellationToken);

        var summary = Assert.Single(batches);
        Assert.Equal(BatchId, summary.BatchId);
        Assert.Equal(BatchName, summary.BatchName);
        Assert.Equal(State, summary.State);
        Assert.Equal(ExtractionCompleted, summary.ExtractionCompletedCount);
        Assert.Equal(ExtractionTotal, summary.ExtractionTotalCount);
        Assert.Equal(EvidenceCompleted, summary.EvidenceCompletedCount);
        Assert.Equal(EmbeddingCompleted, summary.EmbeddingCompletedCount);
        Assert.Equal(EmbeddingTotal, summary.EmbeddingTotalCount);
    }

    private static SagaRow BuildSaga() => new()
    {
        Id = BatchId,
        BatchId = BatchId,
        BatchName = BatchName,
        State = State,
        Engine = "dual",
        CreatedAt = TestData.FixedTime,
        TotalDocumentCount = ExtractionTotal,
        CompletedCount = ExtractionCompleted,
        EvidenceCompletedCount = EvidenceCompleted,
        CompletedAssetEmbeddingUnits = EmbeddingCompleted,
        ExpectedAssetEmbeddingUnits = EmbeddingTotal,
        OrgId = TestIdentity.OrgId,
        OwnerUserId = TestIdentity.UserId,
        ExtractionModel = "m",
        PrimaryEvidenceModel = "m",
        ScoringModel = "m",
        SeedingModel = "m",
        SeedingMode = "legacy",
        WantsHarvesting = true,
        WantsSeeding = true
    };
}
