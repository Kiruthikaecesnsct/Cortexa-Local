using System.Net;
using System.Text.Json;
using Collector.Domain.History;
using Collector.Domain.Serialization;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Api;

public class CollectorBatchesEndpointTests
{
    private const string BatchesPath = "/collector/batches";

    [Fact]
    public async Task Get_Batches_ReturnsOnlyCallersBatches()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        SeedSaga(factory, "batch-other", TestIdentity.OtherUserId, TestIdentity.OrgId);

        using var response = await factory.GetAsync(BatchesPath, TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var batches = await ReadBatchesAsync(response);
        var batch = Assert.Single(batches);
        Assert.Equal("batch-mine", batch.BatchId);
    }

    [Fact]
    public async Task Get_Batches_NoToken_Returns401()
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.GetAsync(BatchesPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Results_ForForeignBatch_Returns404()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-other", TestIdentity.OtherUserId, TestIdentity.OrgId);

        using var response = await factory.GetAsync($"{BatchesPath}/batch-other/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Results_ForMissingBatch_Returns404()
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.GetAsync($"{BatchesPath}/does-not-exist/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Results_ForSameUserDifferentOrg_Returns404()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OtherOrgId);

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Results_TokenMissingOrgClaim_Returns403()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var token = TestJwtFactory.Create(new TokenSpec { OrgId = null });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Batches_ReturnsStageAndHarvestingSeedingCounts()
    {
        await using var factory = new CollectorServerFactory();
        factory.Store.Sagas["batch-mine"] = TestSagas.WithDocumentStates(
            "batch-mine",
            wantsHarvesting: true,
            wantsSeeding: true,
            RowConstants.SagaDocumentStateHarvested,
            RowConstants.SagaDocumentStateSeeded);

        using var response = await factory.GetAsync(BatchesPath, TestJwtFactory.Create());

        var batch = Assert.Single(await ReadBatchesAsync(response));
        Assert.Equal(BatchStage.Harvested, batch.Stage);
        Assert.Equal(2, batch.HarvestingCompletedCount);
        Assert.Equal(2, batch.HarvestingTotalCount);
        Assert.Equal(1, batch.SeedingCompletedCount);
        Assert.Equal(2, batch.SeedingTotalCount);
        Assert.Contains("\"stage\":\"Harvested\"", await ReadRawAsync(response));
    }

    [Fact]
    public async Task Get_Results_ReturnsHarvestingAndSeedingCandidatesWithVerdictAndEvidence()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunkOne = ResultRows.Chunk("doc-1", 0, pageNumber: 5);
        var chunkTwo = ResultRows.Chunk("doc-2", 0, source: new Collector.Domain.Knowledge.KnowledgeSource
        {
            FilePath = "src/a.py",
            LineStart = 10,
            LineEnd = 20
        });
        factory.Store.Chunks.AddRange([chunkOne, chunkTwo]);
        factory.Store.HarvestingCandidates.Add(
            ResultRows.Harvesting("cand-h", weightedScore: 0.4, patentability: 40, ResultRows.Link("doc-1", 0)));
        factory.Store.SeedingReport = ResultRows.Report(
            ResultRows.Opportunity("cand-s", weightedScore: 0.3, patentability: null, chunkTwo.Id));
        factory.Store.Verdicts.Add(ResultRows.Verdict("cand-h", composite: 0.82, patentability: 77));
        factory.Store.EvidenceCounts.AddRange([ResultRows.Evidence("cand-h", 2), ResultRows.Evidence("cand-h", 3)]);

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        Assert.Equal("batch-mine", results.BatchId);
        var harvesting = Assert.Single(results.Candidates, candidate => candidate.Engine == "harvesting");
        Assert.Equal(5, harvesting.EvidenceCount);
        Assert.Equal(0.82, harvesting.Score);
        Assert.Equal(77, harvesting.Patentability);
        var harvestingLink = Assert.Single(harvesting.KnowledgeLinks);
        Assert.Equal("doc-1", harvestingLink.Source.DocumentId);
        Assert.Equal(5, harvestingLink.Source.PageNumber);
        var seeding = Assert.Single(results.Candidates, candidate => candidate.Engine == "seeding");
        Assert.Equal(0.3, seeding.Score);
        Assert.Null(seeding.Patentability);
        Assert.Equal("src/a.py", Assert.Single(seeding.KnowledgeLinks).Source.FilePath);
    }

    [Fact]
    public async Task Get_Results_WithNoReportRows_ReturnsEmptyCandidates()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadResultsAsync(response)).Candidates);
    }

    private static void SeedSaga(CollectorServerFactory factory, string batchId, string ownerUserId, string orgId) =>
        factory.Store.Sagas[batchId] = new SagaRow
        {
            Id = batchId,
            BatchId = batchId,
            BatchName = "Batch",
            Engine = "dual",
            CreatedAt = TestData.FixedTime,
            TotalDocumentCount = 1,
            OrgId = orgId,
            OwnerUserId = ownerUserId,
            ExtractionModel = "m",
            PrimaryEvidenceModel = "m",
            ScoringModel = "m",
            SeedingModel = "m",
            SeedingMode = "legacy",
            WantsHarvesting = true,
            WantsSeeding = true
        };

    private static async Task<string> ReadRawAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyList<BatchSummary>> ReadBatchesAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<IReadOnlyList<BatchSummary>>(await ReadRawAsync(response), CollectorJson.Options)!;

    private static async Task<BatchResults> ReadResultsAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<BatchResults>(await ReadRawAsync(response), CollectorJson.Options)!;
}
