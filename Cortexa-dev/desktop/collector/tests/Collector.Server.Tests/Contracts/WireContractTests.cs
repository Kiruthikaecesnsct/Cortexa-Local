using System.Text.Json;
using System.Text.Json.Nodes;
using Collector.Domain.Serialization;
using Collector.Server.Application.Building;
using Collector.Server.Application.Commands;
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

    private static Collector.Server.Application.Rows.DocumentRow BuildDocument(BatchDocumentInput input)
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
