using Anthropic;
using Anthropic.Core;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class AnthropicClientFactory(ISecretStore secrets, IOptions<AiProviderOptions> options)
{
    public const string MissingKeyMessage = "Add your Claude key in Settings.";

    private readonly object gate = new();
    private (string Key, IAnthropicClient Client)? cached;

    public async Task<IAnthropicClient> CreateAsync(CancellationToken cancellationToken)
    {
        var key = await secrets.ReadAsync(SecretSlot.AnthropicApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, MissingKeyMessage);
        }

        lock (gate)
        {
            if (cached is { } current && string.Equals(current.Key, key, StringComparison.Ordinal))
            {
                return current.Client;
            }

            var client = Build(key);
            cached = (key, client);
            return client;
        }
    }

    private AnthropicClient Build(string key)
    {
        var settings = options.Value;
        return new AnthropicClient(new ClientOptions
        {
            ApiKey = key,
            MaxRetries = settings.MaxRetries,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        });
    }
}
