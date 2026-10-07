using System.Net;
using System.Text.Json;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Serialization;
using Collector.Server.Application.Building;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;

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
    public async Task Get_Results_SkipsResultWhoseChunkIsMissingFromBatch()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunk = SeedChunk(factory, "batch-mine", "doc-1", 0, pageNumber: 5);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-1",
            SourceChunkIndex = 0
        });
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-missing",
            SourceChunkIndex = 0
        });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        var result = Assert.Single(results);
        Assert.Equal(chunk.Id, result.KnowledgeItem.Id);
        Assert.Equal("T", result.KnowledgeItem.Title);
    }

    [Fact]
    public async Task Get_Results_JoinsKnowledgeItemAndSourceFromChunk()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunk = SeedChunk(factory, "batch-mine", "doc-1", 0, pageNumber: 5);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-1",
            SourceChunkIndex = 0
        });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        var result = Assert.Single(results);
        Assert.Equal("harvesting", result.Engine);
        Assert.Equal(chunk.Id, result.KnowledgeItem.Id);
        Assert.Equal("T", result.KnowledgeItem.Title);
        Assert.Equal("S", result.KnowledgeItem.Summary);
        Assert.Equal(5, result.Source.PageNumber);
    }

    [Fact]
    public async Task Get_Results_ForCodeSource_ReturnsFilePathAndLines()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunk = SeedChunk(factory, "batch-mine", "doc-2", 0, pageNumber: null, filePath: "src/a.py", lineStart: 10, lineEnd: 20);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow { Engine = "seeding", ChunkId = chunk.Id });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        var results = await ReadResultsAsync(response);
        var result = Assert.Single(results);
        Assert.Equal("seeding", result.Engine);
        Assert.Equal("src/a.py", result.Source.FilePath);
        Assert.Equal(10, result.Source.LineStart);
        Assert.Equal(20, result.Source.LineEnd);
    }

    [Fact]
    public async Task Get_Results_HarvestingCandidateWithTwoProvenanceLinks_ReturnsTwoResultRows()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunkOne = SeedChunk(factory, "batch-mine", "doc-1", 0, pageNumber: 1);
        var chunkTwo = SeedChunk(factory, "batch-mine", "doc-1", 1, pageNumber: 2);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-1",
            SourceChunkIndex = 0
        });
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-1",
            SourceChunkIndex = 1
        });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.KnowledgeItem.Id == chunkOne.Id);
        Assert.Contains(results, result => result.KnowledgeItem.Id == chunkTwo.Id);
        Assert.All(results, result => Assert.Equal("harvesting", result.Engine));
    }

    [Fact]
    public async Task Get_Results_SeedingOpportunityWithTwoChunkIds_ReturnsTwoResultRows()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunkOne = SeedChunk(factory, "batch-mine", "doc-2", 0, pageNumber: 1);
        var chunkTwo = SeedChunk(factory, "batch-mine", "doc-2", 1, pageNumber: 2);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow { Engine = "seeding", ChunkId = chunkOne.Id });
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow { Engine = "seeding", ChunkId = chunkTwo.Id });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.KnowledgeItem.Id == chunkOne.Id);
        Assert.Contains(results, result => result.KnowledgeItem.Id == chunkTwo.Id);
        Assert.All(results, result => Assert.Equal("seeding", result.Engine));
    }

    [Fact]
    public async Task Get_Results_ProvenanceLinkWithoutMatchingChunk_IsSkipped()
    {
        await using var factory = new CollectorServerFactory();
        SeedSaga(factory, "batch-mine", TestIdentity.UserId, TestIdentity.OrgId);
        var chunk = SeedChunk(factory, "batch-mine", "doc-1", 0, pageNumber: 1);
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow
        {
            Engine = "harvesting",
            DocumentId = "doc-1",
            SourceChunkIndex = 0
        });
        factory.Store.AddResult("batch-mine", new BatchResultJoinRow { Engine = "seeding", ChunkId = "doc-missing|0" });

        using var response = await factory.GetAsync($"{BatchesPath}/batch-mine/results", TestJwtFactory.Create());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await ReadResultsAsync(response);
        var result = Assert.Single(results);
        Assert.Equal(chunk.Id, result.KnowledgeItem.Id);
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

    private static ChunkRow SeedChunk(
        CollectorServerFactory factory,
        string batchId,
        string documentId,
        int orderIndex,
        int? pageNumber,
        string? filePath = null,
        int? lineStart = null,
        int? lineEnd = null)
    {
        var chunk = new ChunkRow
        {
            Id = ChunkRowBuilder.BuildChunkId(documentId, orderIndex),
            BatchId = batchId,
            DocumentId = documentId,
            Text = "text",
            OrderIndex = orderIndex,
            StartChar = 0,
            EndChar = 4,
            TokenCount = 1,
            PageNumber = pageNumber,
            Knowledge = new ChunkKnowledge
            {
                Kind = KnowledgeKind.Logic,
                UnitKind = UnitKind.Section,
                Title = "T",
                Summary = "S",
                Source = new KnowledgeSource
                {
                    PageNumber = pageNumber,
                    FilePath = filePath,
                    LineStart = lineStart,
                    LineEnd = lineEnd
                },
                Provider = CollectorProvider.Claude,
                Model = "m",
                PromptVersion = "v1"
            }
        };
        factory.Store.Chunks.Add(chunk);
        return chunk;
    }

    private static async Task<IReadOnlyList<BatchSummaryDto>> ReadBatchesAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<IReadOnlyList<BatchSummaryDto>>(json, CollectorJson.Options)!;
    }

    private static async Task<IReadOnlyList<BatchResultDto>> ReadResultsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<IReadOnlyList<BatchResultDto>>(json, CollectorJson.Options)!;
    }
}
