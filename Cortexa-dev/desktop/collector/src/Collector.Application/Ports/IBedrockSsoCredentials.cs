using Collector.Application.Secrets;

namespace Collector.Application.Ports;

public interface IBedrockSsoCredentials
{
    Task<BedrockSsoStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
