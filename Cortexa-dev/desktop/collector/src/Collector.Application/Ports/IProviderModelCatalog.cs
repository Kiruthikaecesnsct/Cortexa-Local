using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IProviderModelCatalog
{
    IReadOnlyList<string> ModelsFor(CollectorProvider provider);

    string? DefaultModelFor(CollectorProvider provider);
}
