using Cortexa.ModelRouter.Application.Configuration;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Interfaces;

namespace Cortexa.ModelRouter.Application.Services;

public sealed class ModelCatalog : IModelCatalog
{
    private readonly ModelCatalogSettings _settings;

    public ModelCatalog(ModelCatalogSettings settings)
    {
        _settings = settings;
    }

    public ModelsResponse Get()
    {
        var models = ConvertToModelInfo();
        var dualModeAvailable = IsDualModeAvailable(models);
        var singleDefault = ResolveSingleDefault(models);
        var dualDefaults = ResolveDualDefaults(models);

        return new ModelsResponse(
            models,
            dualModeAvailable,
            new ModelDefaults(singleDefault, dualDefaults));
    }

    public ModelResolution? Resolve(string modelId)
    {
        var entry = FindEntry(modelId);
        if (entry is null)
            return null;

        var deployment = string.IsNullOrWhiteSpace(entry.Deployment) ? entry.Id : entry.Deployment;
        return new ModelResolution(entry.Provider, deployment, entry.Enabled);
    }

    public bool HasEnabledProvider(string providerKey) =>
        _settings.Models.Any(e => e.Enabled && e.Provider == providerKey);

    public ModelResolution? ResolveEnabledSecondary()
    {
        var models = ConvertToModelInfo();
        var secondaryId = ResolveSecondaryDefault(models);
        return secondaryId is null ? null : Resolve(secondaryId);
    }

    private ModelCatalogEntry? FindEntry(string modelId)
    {
        var exact = _settings.Models.FirstOrDefault(e => e.Id == modelId);
        if (exact is not null)
            return exact;

        if (!_settings.Aliases.TryGetValue(modelId, out var aliasTargetId))
            return null;

        return _settings.Models.FirstOrDefault(e => e.Id == aliasTargetId);
    }

    private IReadOnlyList<ModelInfo> ConvertToModelInfo()
    {
        return _settings.Models
            .Select(e => new ModelInfo(
                e.Id,
                e.Label,
                e.Provider,
                e.Role,
                e.Enabled,
                e.Capabilities.AsReadOnly(),
                e.AllowedStages.AsReadOnly()))
            .ToList()
            .AsReadOnly();
    }

    private bool IsDualModeAvailable(IReadOnlyList<ModelInfo> models)
    {
        return models.Any(HasEnabledPrimary) && models.Any(HasEnabledSecondary);
    }

    private string ResolveSingleDefault(IReadOnlyList<ModelInfo> models)
    {
        var defaultId = _settings.SingleDefault;
        if (!string.IsNullOrWhiteSpace(defaultId) && IsModelEnabled(models, defaultId))
            return defaultId;

        var firstEnabled = models.FirstOrDefault(m => m.Enabled);
        if (firstEnabled != null)
            return firstEnabled.Id;

        return models.FirstOrDefault()?.Id ?? string.Empty;
    }

    private DualDefault ResolveDualDefaults(IReadOnlyList<ModelInfo> models)
    {
        var primaryId = ResolvePrimaryDefault(models);
        var secondaryId = ResolveSecondaryDefault(models);
        return new DualDefault(primaryId, secondaryId);
    }

    private string ResolvePrimaryDefault(IReadOnlyList<ModelInfo> models)
    {
        var defaultId = _settings.SingleDefault;
        if (!string.IsNullOrWhiteSpace(defaultId) &&
            IsModelEnabledWithRole(models, defaultId, "primary"))
            return defaultId;

        var firstPrimary = models.FirstOrDefault(HasEnabledPrimary);
        return firstPrimary?.Id ?? string.Empty;
    }

    private string? ResolveSecondaryDefault(IReadOnlyList<ModelInfo> models)
    {
        var firstSecondary = models.FirstOrDefault(HasEnabledSecondary);
        return firstSecondary?.Id;
    }

    private bool IsModelEnabled(IReadOnlyList<ModelInfo> models, string id)
    {
        return models.Any(m => m.Id == id && m.Enabled);
    }

    private bool IsModelEnabledWithRole(
        IReadOnlyList<ModelInfo> models,
        string id,
        string role)
    {
        return models.Any(m => m.Id == id && m.Enabled && m.Role == role);
    }

    private bool HasEnabledPrimary(ModelInfo model)
    {
        return model.Enabled && model.Role == "primary";
    }

    private bool HasEnabledSecondary(ModelInfo model)
    {
        return model.Enabled && model.Role == "secondary";
    }
}
