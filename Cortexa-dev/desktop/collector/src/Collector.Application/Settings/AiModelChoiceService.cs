using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Collector.Application.Settings;

public sealed class AiModelChoiceService
{
    private readonly IUserSettingsStore _store;
    private readonly IProviderModelCatalog _catalog;
    private readonly CollectorProvider _configuredProvider;
    private AiModelChoice _current;

    public AiModelChoiceService(
        IUserSettingsStore store,
        IProviderModelCatalog catalog,
        IOptions<KnowledgeExtractionOptions> extraction)
    {
        _store = store;
        _catalog = catalog;
        _configuredProvider = extraction.Value.Provider;
        _current = Resolve(store.GetAiModelChoice());
    }

    public event EventHandler? Changed;

    public AiModelChoice Current => _current;

    public IReadOnlyList<string> ModelsFor(CollectorProvider provider) => _catalog.ModelsFor(provider);

    public string? DefaultModelFor(CollectorProvider provider) => _catalog.DefaultModelFor(provider);

    public async Task<bool> SaveAsync(AiModelChoice choice, CancellationToken cancellationToken)
    {
        if (!IsListed(choice))
        {
            return false;
        }

        await _store.SaveAiModelChoiceAsync(choice, cancellationToken);
        _current = choice;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private AiModelChoice Resolve(AiModelChoice? saved)
    {
        if (saved is not null && IsListed(saved))
        {
            return saved;
        }

        return new AiModelChoice(_configuredProvider, _catalog.DefaultModelFor(_configuredProvider) ?? string.Empty);
    }

    private bool IsListed(AiModelChoice choice) =>
        _catalog.ModelsFor(choice.Provider).Contains(choice.Model, StringComparer.Ordinal);
}
