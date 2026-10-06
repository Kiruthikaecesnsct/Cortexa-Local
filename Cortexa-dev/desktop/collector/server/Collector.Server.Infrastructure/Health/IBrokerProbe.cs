namespace Collector.Server.Infrastructure.Health;

public interface IBrokerProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
