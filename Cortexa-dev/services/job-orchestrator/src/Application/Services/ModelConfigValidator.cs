using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Services;

public static class ModelStage
{
    public const string Extraction = "extraction";
    public const string Evidence = "evidence";
    public const string Scoring = "scoring";
    public const string Seeding = "seeding";
}

// The model chosen for each pipeline stage, carried as one value so callers
// cannot transpose the positional model ids.
public sealed record StageModelSelection(
    string ExtractionModel,
    string EvidenceModel,
    string ScoringModel,
    string SeedingModel);

public sealed class ModelConfigValidator
{
    private readonly IModelCatalogClient _catalogClient;

    public ModelConfigValidator(IModelCatalogClient catalogClient)
    {
        _catalogClient = catalogClient;
    }

    public static string? ValidateSeedingMode(string? seedingMode)
    {
        if (SeedingModes.IsValid(seedingMode))
            return null;

        return $"Seeding_Mode '{seedingMode}' is not allowed. Allowed values: {SeedingModes.Legacy}, {SeedingModes.Deep}";
    }

    public async Task<ValidationResult> ValidateAsync(StageModelSelection selection, CancellationToken ct)
    {
        IReadOnlyList<CatalogModel> catalogModels;

        try
        {
            catalogModels = await _catalogClient.GetModelsAsync(ct);
        }
        catch
        {
            return ValidationResult.Unavailable();
        }

        var stagesToCheck = new[]
        {
            (ModelStage.Extraction, selection.ExtractionModel),
            (ModelStage.Evidence, selection.EvidenceModel),
            (ModelStage.Scoring, selection.ScoringModel),
            (ModelStage.Seeding, selection.SeedingModel),
        };

        var errors = new List<StageValidationError>();
        foreach (var (stage, modelId) in stagesToCheck)
            CheckStage(stage, modelId, catalogModels, errors);

        return errors.Count > 0
            ? ValidationResult.Invalid(errors)
            : ValidationResult.Valid();
    }

    private static void CheckStage(
        string stage,
        string modelId,
        IReadOnlyList<CatalogModel> catalogModels,
        List<StageValidationError> errors)
    {
        var allowedForStage = catalogModels
            .Where(m => m.Enabled && m.AllowedStages.Contains(stage, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();

        var isValid = allowedForStage.Contains(modelId, StringComparer.OrdinalIgnoreCase);

        if (!isValid)
            errors.Add(new StageValidationError(stage, modelId, allowedForStage));
    }
}

public sealed record StageValidationError(string Stage, string Model, IReadOnlyList<string> AllowedModelIds)
{
    public string ToMessage() =>
        $"Model {Model} is not allowed for stage '{Stage}'. Allowed models for {Stage}: {string.Join(", ", AllowedModelIds)}";
}

public sealed record ValidationResult(
    bool IsValid,
    bool IsCatalogUnavailable,
    IReadOnlyList<StageValidationError>? Errors)
{
    public static ValidationResult Valid() => new(true, false, null);
    public static ValidationResult Invalid(IReadOnlyList<StageValidationError> errors) => new(false, false, errors);
    public static ValidationResult Unavailable() => new(false, true, null);

    public string BuildErrorMessage()
    {
        if (IsCatalogUnavailable)
            return "Model catalog is unavailable; configuration was not saved";

        if (Errors is null || Errors.Count == 0)
            return "Validation failed";

        return string.Join(" ", Errors.Select(e => e.ToMessage()));
    }
}
