using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosBatchExistenceQuery : IBatchExistenceQuery
{
    private const string IdQuery = "SELECT VALUE c.id FROM c";

    private readonly Container _container;

    public CosmosBatchExistenceQuery(CosmosClient client, IOptions<CosmosSettings> settings)
    {
        var s = settings.Value;
        _container = client.GetContainer(s.Database, s.BatchesContainer);
    }

    public async Task<IReadOnlySet<string>> GetLiveBatchIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var iterator = _container.GetItemQueryIterator<string>(new QueryDefinition(IdQuery));

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var id in page)
                ids.Add(id);
        }

        return ids;
    }
}
