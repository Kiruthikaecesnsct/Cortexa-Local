namespace Collector.Server.Application.Upload;

public sealed class ModelDefaultsOptions
{
    public string ExtractionModel { get; set; } = string.Empty;

    public string PrimaryEvidenceModel { get; set; } = string.Empty;

    public string ScoringModel { get; set; } = string.Empty;

    public string SeedingModel { get; set; } = string.Empty;

    public string SeedingMode { get; set; } = string.Empty;
}
