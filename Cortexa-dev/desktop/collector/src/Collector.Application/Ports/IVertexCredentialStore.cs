namespace Collector.Application.Ports;

public interface IVertexCredentialStore
{
    Task<string?> GetServiceAccountJsonAsync(CancellationToken cancellationToken);

    Task SaveServiceAccountJsonAsync(string serviceAccountJson, CancellationToken cancellationToken);
}
