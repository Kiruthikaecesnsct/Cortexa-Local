using Collector.Server.Application.Upload;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Upload;

public class UploadPreparerTests
{
    private const string BatchId = "batch-42";

    private readonly UploadPipeline _pipeline = new();

    [Fact]
    public async Task PrepareAsync_ValidRequest_MapsCallerEngineModelsAndTime()
    {
        var preparation = await PrepareAsync(UploadRequests.Valid());

        var saga = preparation.Command!.Saga;
        Assert.Equal(TestIdentity.OrgId, saga.OrgId);
        Assert.Equal(TestIdentity.UserId, saga.OwnerUserId);
        Assert.Equal(UploadTestOptions.ConfiguredEngine.ToLowerInvariant(), saga.Engine);
        Assert.Equal(FakeModelConfigReader.DefaultSnapshot.SeedingMode, saga.SeedingMode);
        Assert.Equal(TestData.FixedTime, saga.CreatedAt);
        Assert.Equal(BatchId, preparation.Command.BatchId);
    }

    [Fact]
    public async Task PrepareAsync_ValidRequest_DocumentIdsAreDerivedFromBatchAndClientId()
    {
        var preparation = await PrepareAsync(UploadRequests.Valid());

        var document = Assert.Single(preparation.Command!.Documents);
        Assert.Equal(DeterministicIds.DocumentId(BatchId, UploadRequests.ClientId(1)), document.DocumentId);
    }

    [Fact]
    public async Task PrepareAsync_InvalidRequest_ReturnsErrorsWithoutReadingModels()
    {
        var request = UploadRequests.Valid() with { BatchName = string.Empty };

        var preparation = await PrepareAsync(request);

        Assert.Null(preparation.Command);
        Assert.Equal("batch_name", Assert.Single(preparation.Errors).Field);
        Assert.Equal(0, _pipeline.Models.CallCount);
    }

    private Task<UploadPreparation> PrepareAsync(Collector.Domain.Upload.KnowledgeUploadRequest request) =>
        _pipeline.Preparer.PrepareAsync(request, TestIdentity.Caller, BatchId, TestContext.Current.CancellationToken);
}
