namespace Cortexa.JobOrchestrator.Application.Contracts;

public sealed record CatalogModel(string Id, bool Enabled, IReadOnlyList<string> AllowedStages);

public interface IModelCatalogClient
{
    // Returns the full model catalog in catalog order (as the model-router returns them),
    // including each model's enabled flag and per-stage allow-list.
    Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CancellationToken ct);
}
