using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosPipelineOrphanScanner : IOrphanScanner
{
    private const string BatchIdQuery = "SELECT DISTINCT VALUE c.batch_id FROM c";

    private readonly CosmosClient _client;
    private readonly string _database;
    private readonly IReadOnlyList<string> _containerNames;
    private readonly ILogger<CosmosPipelineOrphanScanner> _logger;

    public CosmosPipelineOrphanScanner(
        CosmosClient client,
        IOptions<CosmosSettings> settings,
        ILogger<CosmosPipelineOrphanScanner> logger)
    {
        var s = settings.Value;
        _client = client;
        _database = s.Database;
        _logger = logger;
        _containerNames = new[]
        {
            s.DocumentsContainer,
            s.ChunksContainer,
            s.ProvenanceMapsContainer,
            s.CandidatesContainer,
            s.EvidenceBundlesContainer,
            s.VerdictsContainer,
            s.SeedingContainer,
            s.HarvestingContainer
        }.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
    }

    public string StoreName => "cosmos-pipeline";

    public async Task<IReadOnlyCollection<string>> ListBatchIdsAsync(CancellationToken ct)
    {
        var allIds = new HashSet<string>();

        foreach (var containerName in _containerNames)
        {
            try
            {
                var ids = await QueryBatchIdsAsync(containerName, ct);
                foreach (var id in ids)
                    allIds.Add(id);
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning(
                    "Pipeline orphan scan failed for container. container={Container} status={Status}",
                    containerName,
                    ex.StatusCode);
            }
        }

        return allIds;
    }

    private async Task<IReadOnlyList<string>> QueryBatchIdsAsync(string containerName, CancellationToken ct)
    {
        var container = _client.GetContainer(_database, containerName);
        var iterator = container.GetItemQueryIterator<string>(new QueryDefinition(BatchIdQuery));
        var ids = new List<string>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            ids.AddRange(page.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        return ids;
    }
}
