using Amazon;
using Amazon.BedrockRuntime;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class BedrockClientFactory(
    BedrockSsoCredentials credentials,
    IOptions<BedrockProviderOptions> options) : IDisposable
{
    private readonly object gate = new();
    private (string Region, AmazonBedrockRuntimeClient Client)? cached;

    public Task<IAmazonBedrockRuntime> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;

        lock (gate)
        {
            if (cached is { } current && string.Equals(current.Region, settings.Region, StringComparison.Ordinal))
            {
                return Task.FromResult<IAmazonBedrockRuntime>(current.Client);
            }

            cached?.Client.Dispose();
            var client = Build(settings);
            cached = (settings.Region, client);
            return Task.FromResult<IAmazonBedrockRuntime>(client);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            cached?.Client.Dispose();
            cached = null;
        }
    }

    private AmazonBedrockRuntimeClient Build(BedrockProviderOptions settings) => new(
        credentials,
        new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region),
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            MaxErrorRetry = settings.MaxRetries,
        });
}
