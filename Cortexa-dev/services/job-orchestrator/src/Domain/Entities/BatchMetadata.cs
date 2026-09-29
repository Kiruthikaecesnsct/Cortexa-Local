namespace Cortexa.JobOrchestrator.Domain.Entities;

public sealed record BatchMetadata(
    string? BatchName = null,
    string? Engine = null,
    string? AiModel = null,
    string? SeedCorpusDomain = null,
    string? GitRepoUrl = null,
    string? GitPatSecretName = null,
    string? GitBranch = null,
    string? GitHost = null,
    DateTimeOffset? CreatedAt = null,
    int TotalDocumentCount = 0,
    string? OwnerOrgId = null,
    string? OwnerUserId = null,
    string? ExtractionModel = null,
    string? PrimaryEvidenceModel = null,
    string? ScoringModel = null,
    string? SeedingModel = null,
    string? SeedingMode = null);
