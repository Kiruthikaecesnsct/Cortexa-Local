namespace Collector.Server.Application.Upload;

public sealed record ModelConfigSnapshot(
    string ExtractionModel,
    string PrimaryEvidenceModel,
    string ScoringModel,
    string SeedingModel,
    string SeedingMode);
