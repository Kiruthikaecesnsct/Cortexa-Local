using Collector.Server.Application.Ports;

namespace Collector.Server.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
