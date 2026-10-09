using Collector.Application.Secrets;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiClientFactory(
    IHttpClientFactory httpClients,
    IOptions<GeminiProviderOptions> options) : IDisposable
{
    public const string MissingKeyMessage = "Add your Gemini key in Settings.";

    private readonly object gate = new();
    private readonly Dictionary<string, Client> cached = [];

    public Client GetOrCreate(IReadOnlyList<GeminiKey> keys, string keyId)
    {
        lock (gate)
        {
            PruneLocked(keys);
            if (cached.TryGetValue(keyId, out var existing))
            {
                return existing;
            }

            var key = keys.First(candidate => candidate.Id == keyId).Key;
            var client = Build(key);
            cached[keyId] = client;
            return client;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var client in cached.Values)
            {
                client.Dispose();
            }

            cached.Clear();
        }
    }

    private void PruneLocked(IReadOnlyList<GeminiKey> keys)
    {
        var validIds = keys.Select(key => key.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in cached.Keys.Where(id => !validIds.Contains(id)).ToArray())
        {
            cached[staleId].Dispose();
            cached.Remove(staleId);
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
