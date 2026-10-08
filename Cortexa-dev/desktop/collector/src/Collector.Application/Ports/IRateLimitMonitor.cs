using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRateLimitMonitor
{
    event EventHandler<RateLimitStatus>? StatusChanged;

    RateLimitStatus GetStatus(SourceType provider);
}
