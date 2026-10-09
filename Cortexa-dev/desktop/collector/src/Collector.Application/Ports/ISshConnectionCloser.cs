namespace Collector.Application.Ports;

public interface ISshConnectionCloser
{
    Task CloseAsync(CancellationToken cancellationToken);
}
