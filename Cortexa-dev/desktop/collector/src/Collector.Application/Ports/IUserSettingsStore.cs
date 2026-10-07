using Collector.Application.Settings;

namespace Collector.Application.Ports;

public interface IUserSettingsStore
{
    EndpointSettings GetEndpoints();

    Task SaveEndpointsAsync(EndpointSettings settings, CancellationToken cancellationToken);
}
