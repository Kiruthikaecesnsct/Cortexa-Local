using Cortexa.ModelRouter.Application.DTOs;

namespace Cortexa.ModelRouter.Application.Interfaces;

public interface IModelCatalog
{
    ModelsResponse Get();
    ModelResolution? Resolve(string modelId);
    bool HasEnabledProvider(string providerKey);
    ModelResolution? ResolveEnabledSecondary();
}
