using Azure.Identity;
using Collector.Domain.Serialization;
using Collector.Server.Infrastructure.Options;
using Microsoft.Azure.Cosmos;

namespace Collector.Server.Infrastructure.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            Serializer = new CollectorCosmosSerializer(CollectorJson.CreateOptions()),
            ConnectionMode = options.ConnectionMode,
            LimitToEndpoint = options.LimitToEndpoint
        };

        return string.IsNullOrWhiteSpace(options.Key)
            ? new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions)
            : new CosmosClient(options.Endpoint, options.Key, clientOptions);
    }
}
