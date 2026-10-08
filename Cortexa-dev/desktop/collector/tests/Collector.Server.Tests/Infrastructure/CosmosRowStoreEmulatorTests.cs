using System.Net;
using System.Text.Json.Nodes;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure;
using Collector.Server.Infrastructure.Cosmos;
using Collector.Server.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Server.Tests.Infrastructure;

public class CosmosRowStoreEmulatorTests
{
    private const string OptInVariable = "COLLECTOR_IT_COSMOS";
    private const string EndpointVariable = "Cosmos__Endpoint";
    private const string KeyVariable = "Cosmos__Key";
    private const string DatabaseName = "cortexa-pipeline";
    private const int KnowledgeItemCount = 3;
    private const int WriteConcurrency = 4;

    [Fact]
    public async Task Handle_WithThreeKnowledgeItems_WritesCortexaShapedRows()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 and a running Cosmos emulator to run this test.");

        var options = BuildOptions();
        using var client = CosmosClientFactory.Create(options);
        var batchId = $"it-{Guid.NewGuid():N}";
        var items = Enumerable.Range(0, KnowledgeItemCount).Select(index => TestData.PlainItem($"Item {index}")).ToArray();

        try
        {
            await WriteAsync(client, options, batchId, items);
            await AssertRowsAsync(client, options, batchId);
        }
        finally
        {
            await CleanUpAsync(client, options, batchId);
        }
    }

    [Fact]
    public async Task Delete_AfterWrite_RemovesEveryRowAndIsIdempotent()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 and a running Cosmos emulator to run this test.");

        var options = BuildOptions();
        using var client = CosmosClientFactory.Create(options);
        var store = new CosmosPipelineRowStore(client, Microsoft.Extensions.Options.Options.Create(options));
        var batchId = $"it-{Guid.NewGuid():N}";
        var items = Enumerable.Range(0, KnowledgeItemCount).Select(index => TestData.PlainItem($"Item {index}")).ToArray();
        var rowIds = Enumerable.Range(0, KnowledgeItemCount).Select(index => $"{TestData.PaperDocumentId}|{index}").ToList();
        var documentIds = new List<string> { TestData.PaperDocumentId };

        try
        {
            await WriteAsync(client, options, batchId, items);

            var firstPass = await DeleteAllAsync(store, batchId, rowIds, documentIds);

            Assert.All(firstPass, failed => Assert.Empty(failed));
            Assert.Null(await store.GetSagaAsync(batchId, TestContext.Current.CancellationToken));
            Assert.Empty(await store.GetDocumentsByBatchAsync(batchId, TestContext.Current.CancellationToken));
            await AssertGoneAsync(client, options, batchId, rowIds);

            var secondPass = await DeleteAllAsync(store, batchId, rowIds, documentIds);

            Assert.All(secondPass, failed => Assert.Empty(failed));
        }
        finally
        {
            await CleanUpAsync(client, options, batchId);
        }
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> DeleteAllAsync(
        CosmosPipelineRowStore store,
        string batchId,
        IReadOnlyList<string> rowIds,
        IReadOnlyList<string> documentIds)
    {
        var token = TestContext.Current.CancellationToken;
        return
        [
            await store.DeleteSagaAsync(batchId, token),
            await store.DeleteProvenanceAsync(batchId, rowIds, token),
            await store.DeleteChunksAsync(batchId, rowIds, token),
            await store.DeleteDocumentsAsync(batchId, documentIds, token)
        ];
    }

    private static async Task AssertGoneAsync(CosmosClient client, CosmosOptions options, string batchId, IReadOnlyList<string> rowIds)
    {
        var key = new PartitionKey(batchId);
        await AssertNotFoundAsync(client, options.DocumentsContainer, TestData.PaperDocumentId, key);

        foreach (var id in rowIds)
        {
            await AssertNotFoundAsync(client, options.ChunksContainer, id, key);
            await AssertNotFoundAsync(client, options.ProvenanceMapsContainer, id, key);
        }
    }

    private static async Task AssertNotFoundAsync(CosmosClient client, string container, string id, PartitionKey key)
    {
        using var response = await client.GetContainer(DatabaseName, container).ReadItemStreamAsync(id, key);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CosmosOptions BuildOptions() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable(EndpointVariable) ?? string.Empty,
        Key = Environment.GetEnvironmentVariable(KeyVariable) ?? string.Empty,
        Database = DatabaseName,
        DocumentsContainer = "documents",
        ChunksContainer = "chunks",
        ProvenanceMapsContainer = "provenance_maps",
        BatchesContainer = "batches",
        ReportsContainer = "reports",
        VerdictsContainer = "verdicts",
        EvidenceBundlesContainer = "evidence_bundles",
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        MaxConcurrentWrites = WriteConcurrency
    };

    private static async Task WriteAsync(CosmosClient client, CosmosOptions options, string batchId, Collector.Domain.Knowledge.KnowledgeItem[] items)
    {
        var store = new CosmosPipelineRowStore(client, Microsoft.Extensions.Options.Options.Create(options));
        var clock = new SystemClock();
        var handler = new WriteKnowledgeBatchHandler(
            store,
            new IngestionEventDispatcher(new NoopPublisher(), clock, NullLogger<IngestionEventDispatcher>.Instance),
            new BatchRollback(store, NullLogger<BatchRollback>.Instance),
            clock,
            NullLogger<WriteKnowledgeBatchHandler>.Instance);

        await handler.HandleAsync(TestData.Command(batchId, TestData.PaperDocument(items)), TestContext.Current.CancellationToken);
    }

    private static async Task AssertRowsAsync(CosmosClient client, CosmosOptions options, string batchId)
    {
        var key = new PartitionKey(batchId);
        var saga = await ReadRawAsync(client, options.BatchesContainer, batchId, key);

        Assert.Equal("InProgress", saga["state"]!.GetValue<string>());
        Assert.Equal("Queued", saga["documents"]![0]!["state"]!.GetValue<string>());
        Assert.Equal(1, saga["total_document_count"]!.GetValue<int>());

        var document = await ReadRawAsync(client, options.DocumentsContainer, TestData.PaperDocumentId, key);
        Assert.Equal("completed", document["status"]!.GetValue<string>());
        Assert.Equal(KnowledgeItemCount, document["chunk_count"]!.GetValue<int>());

        for (var index = 0; index < KnowledgeItemCount; index++)
        {
            var id = $"{TestData.PaperDocumentId}|{index}";
            var chunk = await ReadRawAsync(client, options.ChunksContainer, id, key);
            Assert.Equal(index, chunk["order_index"]!.GetValue<int>());
            var provenance = await ReadRawAsync(client, options.ProvenanceMapsContainer, id, key);
            Assert.Equal("paper", provenance["source_kind"]!.GetValue<string>());
        }
    }

    private static async Task<JsonNode> ReadRawAsync(CosmosClient client, string container, string id, PartitionKey key)
    {
        var target = client.GetContainer(DatabaseName, container);
        using var response = await target.ReadItemStreamAsync(id, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(response.Content)!;
    }

    private static async Task CleanUpAsync(CosmosClient client, CosmosOptions options, string batchId)
    {
        var key = new PartitionKey(batchId);
        await DeleteQuietlyAsync(client, options.BatchesContainer, batchId, key);
        await DeleteQuietlyAsync(client, options.DocumentsContainer, TestData.PaperDocumentId, key);

        for (var index = 0; index < KnowledgeItemCount; index++)
        {
            var id = $"{TestData.PaperDocumentId}|{index}";
            await DeleteQuietlyAsync(client, options.ChunksContainer, id, key);
            await DeleteQuietlyAsync(client, options.ProvenanceMapsContainer, id, key);
        }
    }

    private static async Task DeleteQuietlyAsync(CosmosClient client, string container, string id, PartitionKey key)
    {
        using var response = await client.GetContainer(DatabaseName, container).DeleteItemStreamAsync(id, key);
    }

    private sealed class NoopPublisher : IIngestionEventPublisher
    {
        public Task PublishAsync(Collector.Server.Application.Events.EventEnvelope envelope, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
