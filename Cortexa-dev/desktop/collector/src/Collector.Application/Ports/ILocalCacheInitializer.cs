namespace Collector.Application.Ports;

public interface ILocalCacheInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
