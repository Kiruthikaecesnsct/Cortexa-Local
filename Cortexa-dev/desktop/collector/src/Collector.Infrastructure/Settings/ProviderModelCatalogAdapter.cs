using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Settings;

public sealed class ProviderModelCatalogAdapter(IOptions<ProviderModelCatalog> catalog) : IProviderModelCatalog
{
    public IReadOnlyList<string> ModelsFor(CollectorProvider provider) =>
        Find(provider)?.Models ?? (IReadOnlyList<string>)[];

    public string? DefaultModelFor(CollectorProvider provider)
    {
        var model = Find(provider)?.DefaultModel;
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private ProviderModelCatalogEntry? Find(CollectorProvider provider) =>
        catalog.Value.Providers.GetValueOrDefault(provider);
}
