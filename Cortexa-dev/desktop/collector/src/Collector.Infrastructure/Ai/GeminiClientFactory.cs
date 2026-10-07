using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiClientFactory(
    ISecretStore secrets,
    IHttpClientFactory httpClients,
    IOptions<GeminiProviderOptions> options) : IDisposable
{
    public const string MissingKeyMessage = "Add your Gemini key in Settings.";

    private readonly object gate = new();
    private (string Key, Client Client)? cached;

    public async Task<Client> CreateAsync(CancellationToken cancellationToken)
    {
        var key = await secrets.ReadAsync(SecretSlot.GeminiApiKey, cancellationToken);
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

            cached?.Client.Dispose();
            var client = Build(key);
            cached = (key, client);
            return client;
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

    private Client Build(string key) => new(
        enterprise: false,
        vertexAI: false,
        apiKey: key,
        httpOptions: new HttpOptions
        {
            RetryOptions = new HttpRetryOptions { Attempts = options.Value.MaxRetries + 1 },
        },
        clientOptions: new ClientOptions
        {
            HttpClientFactory = () => httpClients.CreateClient(HttpClientNames.Gemini),
        });
}
