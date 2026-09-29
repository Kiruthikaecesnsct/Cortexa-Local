namespace Cortexa.JobOrchestrator.Domain.Entities;

public static class SeedingModes
{
    public const string Legacy = "legacy";
    public const string Deep = "deep";

    public static bool IsValid(string? value) =>
        string.Equals(value, Legacy, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, Deep, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) =>
        string.Equals(value, Deep, StringComparison.OrdinalIgnoreCase) ? Deep : Legacy;
}

public sealed record ModelConfig(
    string ExtractionModel,
    string PrimaryEvidenceModel,
    string ScoringModel,
    string SeedingModel,
    string SeedingMode,
    DateTimeOffset UpdatedAt);
