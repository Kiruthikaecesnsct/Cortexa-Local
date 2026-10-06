using Collector.Application.Secrets;

namespace Collector.Application.Ports;

public interface ISecretStore
{
    Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken);

    Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken);

    Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken);
}
