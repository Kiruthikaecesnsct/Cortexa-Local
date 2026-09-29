using System.Text.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class SystemTextJsonCosmosSerializerTests
{
    private readonly SystemTextJsonCosmosSerializer _serializer;

    public SystemTextJsonCosmosSerializerTests()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        _serializer = new SystemTextJsonCosmosSerializer(options);
    }

    [Fact]
    public void ToStream_SerializesWithSnakeCasePropertyNames()
    {
        var document = new SagaDocument
        {
            Id = "saga-123",
            BatchId = "batch-456",
            State = "active",
            Version = 1,
            SchemaVersion = 1,
            WantsHarvesting = true,
            WantsSeeding = false,
            ActiveCount = 3,
            CompletedCount = 10,
            TotalDocumentCount = 15,
            BatchName = "Test Batch",
            Engine = "harvesting"
        };

        var stream = _serializer.ToStream(document);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        json.Should().Contain("\"batch_id\"");
        json.Should().Contain("\"id\"");
        json.Should().NotContain("\"batchId\"");
        json.Should().NotContain("\"BatchId\"");
        json.Should().Contain("\"batch-456\"");
        json.Should().Contain("\"saga-123\"");
    }

    [Fact]
    public void FromStream_DeserializesSnakeCaseJsonCorrectly()
    {
        var json = """
        {
            "id": "saga-789",
            "batch_id": "batch-101",
            "state": "completed",
            "version": 2,
            "schema_version": 1,
            "wants_harvesting": false,
            "wants_seeding": true,
            "active_count": 0,
            "completed_count": 20,
            "total_document_count": 20,
            "batch_name": "Another Batch",
            "engine": "seeding"
        }
        """;

        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _serializer.FromStream<SagaDocument>(stream);

        result.Should().NotBeNull();
        result.Id.Should().Be("saga-789");
        result.BatchId.Should().Be("batch-101");
        result.State.Should().Be("completed");
        result.Version.Should().Be(2);
        result.SchemaVersion.Should().Be(1);
        result.WantsHarvesting.Should().BeFalse();
        result.WantsSeeding.Should().BeTrue();
        result.ActiveCount.Should().Be(0);
        result.CompletedCount.Should().Be(20);
        result.TotalDocumentCount.Should().Be(20);
        result.BatchName.Should().Be("Another Batch");
        result.Engine.Should().Be("seeding");
    }

    [Fact]
    public void RoundTrip_PreservesBatchIdAndId()
    {
        var original = new SagaDocument
        {
            Id = "test-id-999",
            BatchId = "test-batch-888",
            State = "ingesting",
            Version = 5,
            SchemaVersion = 1
        };

        var stream = _serializer.ToStream(original);
        stream.Position = 0;
        var deserialized = _serializer.FromStream<SagaDocument>(stream);

        deserialized.Id.Should().Be(original.Id);
        deserialized.BatchId.Should().Be(original.BatchId);
        deserialized.State.Should().Be(original.State);
        deserialized.Version.Should().Be(original.Version);
    }
}
