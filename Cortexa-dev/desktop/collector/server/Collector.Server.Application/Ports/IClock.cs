namespace Collector.Server.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
