using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Secrets;

namespace Collector.Infrastructure.Secrets;

public sealed class VertexCredentialStore(ISecretStore secrets) : IVertexCredentialStore
{
    public Task<string?> GetServiceAccountJsonAsync(CancellationToken cancellationToken) =>
        secrets.ReadAsync(SecretSlot.VertexServiceAccount, cancellationToken);

    public Task SaveServiceAccountJsonAsync(string serviceAccountJson, CancellationToken cancellationToken)
    {
        EnsureValidJson(serviceAccountJson);
        return secrets.WriteAsync(SecretSlot.VertexServiceAccount, serviceAccountJson, cancellationToken);
    }

    private static void EnsureValidJson(string serviceAccountJson)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            throw new SecretStoreException("Vertex service account JSON is required.");
        }

        if (!IsJsonObject(serviceAccountJson))
        {
            throw new SecretStoreException("Vertex service account JSON must be a JSON object.");
        }
    }

    private static bool IsJsonObject(string serviceAccountJson)
    {
        try
        {
            using var document = JsonDocument.Parse(serviceAccountJson);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
