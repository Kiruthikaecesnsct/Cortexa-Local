using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IAiProviderFactory
{
    IAiProvider Resolve(CollectorProvider provider);

    int ConcurrencyFor(CollectorProvider provider);
}
