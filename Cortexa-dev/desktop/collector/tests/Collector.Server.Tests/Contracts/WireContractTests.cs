using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Collector.Domain.Enums;
using Collector.Domain.History;
using Collector.Domain.Serialization;
using Collector.Server.Application.Building;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;
using Collector.Server.Infrastructure.Cosmos;

namespace Collector.Server.Tests.Contracts;

public class WireContractTests
{
    private const string FixedEventId = "event-fixed";
    private const string FixedCorrelationId = "corr-1";

    private static readonly CollectorCosmosSerializer Serializer = new(CollectorJson.CreateOptions());

    [Fact]
    public void Saga_MatchesGoldenShape()
    {
        var saga = SagaRowBuilder.Build(TestData.Command(TestData.PaperDocument()));

        AssertMatchesFixture("saga.json", Serialize(saga));
    }

    [Fact]
    public void Saga_UsesPascalCaseStatesAndNoNulls()
    {
        var saga = SagaRowBuilder.Build(TestData.Command(TestData.PaperDocument()));

        var node = Serialize(saga);

        Assert.Equal("InProgress", node["state"]!.GetValue<string>());
        Assert.Equal("Queued", node["documents"]![0]!["state"]!.GetValue<string>());
        Assert.Empty(FindNulls(node));
    }

    [Fact]
    public void Document_MatchesGoldenShape()
    {
        var document = BuildDocument(TestData.PaperDocument());

        AssertMatchesFixture("document.json", Serialize(document));
    }

    [Fact]
    public void Chunk_MatchesGoldenShape()
    {
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, TestData.PaperDocument(), TestData.Collector());

        AssertMatchesFixture("chunk.json", Serialize(Assert.Single(chunks)));
    }

    [Fact]
    public void PaperProvenance_MatchesGoldenShape()
    {
        var document = TestData.PaperDocument();
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var rows = ProvenanceRowBuilder.Build(chunks, document);

        AssertMatchesFixture("provenance.json", Serialize(Assert.Single(rows)));
    }

    [Fact]
    public void CodeProvenance_MatchesGoldenShape()
    {
        var document = TestData.CodeDocument();
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var rows = ProvenanceRowBuilder.Build(chunks, document);

        AssertMatchesFixture("provenance_code.json", Serialize(Assert.Single(rows)));
    }

    [Fact]
    public void IngestionCompleted_MatchesGoldenShape()
    {
        var envelope = IngestionCompletedEventBuilder.Build(
            BuildDocument(TestData.PaperDocument()),
            FixedCorrelationId,
            TestData.FixedTime) with
        { EventId = FixedEventId };

        var node = JsonSerializer.SerializeToNode(envelope, CollectorJson.Options)!;

        AssertMatchesFixture("ingestion_completed.json", node);
        Assert.Equal(JsonValueKind.Number, node["payload"]!["chunk_count"]!.GetValueKind());
    }

    [Fact]
    public void Verdict_ProjectionSourcePathsExistAndDeserialize()
    {
        var verdict = ReadFixture("verdict.json");
        var projected = new JsonObject
        {
            ["candidate_id"] = verdict["candidate_id"]!.DeepClone(),
            ["composite_score"] = verdict["composite_score"]!.DeepClone(),
            ["patentability"] = verdict["axes"]![RowConstants.PatentabilityAxis]!["score"]!.DeepClone()
        };

        var row = Deserialize<VerdictSummaryRow>(projected);

        Assert.Equal("cand-1", row.CandidateId);
        Assert.Equal(68.0, row.CompositeScore);
        Assert.Equal(72.0, row.Patentability);
        Assert.Equal("batch-1:cand-1", verdict["id"]!.GetValue<string>());
    }

    [Fact]
    public void EvidenceBundle_HitsArrayDrivesHitCount()
    {
        var bundle = ReadFixture("evidence_bundle.json");
        var projected = new JsonObject
        {
            ["candidate_id"] = bundle["candidate_id"]!.DeepClone(),
            ["hit_count"] = bundle["hits"]!.AsArray().Count
        };

        var row = Deserialize<EvidenceCountRow>(projected);

        Assert.Equal("cand-1", row.CandidateId);
        Assert.Equal(3, row.HitCount);
    }

    [Fact]
    public void ReportCandidate_DeserializesIntoTypedRow()
    {
        var row = Deserialize<HarvestingReportCandidateRow>(ReadFixture("report_candidate.json"));

        Assert.Equal("cand-1", row.CandidateId);
        Assert.Equal("Adaptive cache eviction", row.Title);
        Assert.Equal("Mature", row.Maturity);
        Assert.Equal(66.5, row.WeightedScore);
        Assert.Equal(72.0, row.Axes[RowConstants.PatentabilityAxis].Score);
        Assert.Equal(2, row.ProvenanceLinks.Count);
        Assert.Equal(0, row.ProvenanceLinks[0].SourceChunkIndex);
        Assert.Equal("doc-1|2", row.ProvenanceLinks[1].ChunkId);
    }

    [Fact]
    public void SeedingReport_DeserializesOpportunitiesWithGrounding()
    {
        var report = Deserialize<SeedingReportRow>(ReadFixture("seeding_report.json"));

        var opportunity = Assert.Single(report.Opportunities);
        Assert.Equal("cand-2", opportunity.CandidateId);
        Assert.Equal("adjacent", opportunity.Category);
        Assert.Equal(41.0, opportunity.WeightedScore);
        Assert.Equal(38.0, opportunity.Axes[RowConstants.PatentabilityAxis].Score);
        Assert.Equal(["doc-1|0", "doc-1|1"], opportunity.GroundedIn!.ChunkIds);
    }

    [Fact]
    public void BatchSummary_SerializesStageAsPascalCaseStringWithSnakeCaseNames()
    {
        var summary = new BatchSummary
        {
            BatchId = "batch-1",
            BatchName = "Name",
            CreatedAt = TestData.FixedTime,
            State = RowConstants.SagaStateInProgress,
            Stage = BatchStage.Harvested,
            ExtractionCompletedCount = 1,
            ExtractionTotalCount = 2,
            EvidenceCompletedCount = 3,
            EmbeddingCompletedCount = 4,
            EmbeddingTotalCount = 5,
            HarvestingCompletedCount = 6,
            HarvestingTotalCount = 7,
            SeedingCompletedCount = 8,
            SeedingTotalCount = 9
        };

        var node = JsonSerializer.SerializeToNode(summary, CollectorJson.Options)!;

        Assert.Equal("Harvested", node["stage"]!.GetValue<string>());
        Assert.Equal(6, node["harvesting_completed_count"]!.GetValue<int>());
        Assert.Equal(9, node["seeding_total_count"]!.GetValue<int>());
        Assert.Equal(BatchStage.Harvested, JsonSerializer.Deserialize<BatchSummary>(node, CollectorJson.Options)!.Stage);
    }

    [Fact]
    public void BatchResults_SerializesCandidateAndLinkShape()
    {
        var results = new BatchResults
        {
            BatchId = "batch-1",
            Candidates =
            [
                new BatchCandidate
                {
                    CandidateId = "cand-1",
                    Engine = "harvesting",
                    Title = "T",
                    Kind = "Mature",
                    EvidenceCount = 3,
                    Score = 68.0,
                    Patentability = 72,
                    KnowledgeLinks =
                    [
                        new CandidateKnowledgeLink
                        {
                            KnowledgeItem = new LinkedKnowledgeItem
                            {
                                Id = "doc-1|0",
                                Kind = KnowledgeKind.KeyContent,
                                Title = "K",
                                Summary = "S"
                            },
                            Source = new CandidateSource { DocumentId = "doc-1", PageNumber = 4 }
                        }
                    ]
                }
            ]
        };

        var node = JsonSerializer.SerializeToNode(results, CollectorJson.Options)!;

        var candidate = node["candidates"]![0]!;
        Assert.Equal("cand-1", candidate["candidate_id"]!.GetValue<string>());
        Assert.Equal(3, candidate["evidence_count"]!.GetValue<int>());
        Assert.Equal(72, candidate["patentability"]!.GetValue<int>());
        var link = candidate["knowledge_links"]![0]!;
        Assert.Equal("key_content", link["knowledge_item"]!["kind"]!.GetValue<string>());
        Assert.Equal("doc-1", link["source"]!["document_id"]!.GetValue<string>());
        Assert.Equal(4, link["source"]!["page_number"]!.GetValue<int>());
    }

    private static JsonNode ReadFixture(string fixtureName) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "Fixtures", fixtureName)))!;

    private static T Deserialize<T>(JsonNode node) =>
        Serializer.FromStream<T>(new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString())));

    private static DocumentRow BuildDocument(BatchDocumentInput input)
    {
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, input, TestData.Collector());
        return DocumentRowBuilder.Build(TestData.BatchId, input, chunks.Count, TestData.FixedTime);
    }

    private static JsonNode Serialize<T>(T row)
    {
        using var stream = Serializer.ToStream(row);
        return JsonNode.Parse(stream)!;
    }

    private static void AssertMatchesFixture(string fixtureName, JsonNode actual)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Contracts", "Fixtures", fixtureName);
        var expected = JsonNode.Parse(File.ReadAllText(path))!;

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Fixture {fixtureName} differs.{Environment.NewLine}Expected: {expected.ToJsonString()}{Environment.NewLine}Actual: {actual.ToJsonString()}");
    }

    private static List<string> FindNulls(JsonNode? node, string path = "$")
    {
        var found = new List<string>();

        switch (node)
        {
            case null:
                found.Add(path);
                break;
            case JsonObject obj:
                foreach (var property in obj)
                {
                    found.AddRange(FindNulls(property.Value, $"{path}.{property.Key}"));
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    found.AddRange(FindNulls(array[index], $"{path}[{index}]"));
                }

                break;
        }

        return found;
    }
}
