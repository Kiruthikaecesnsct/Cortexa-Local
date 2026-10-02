namespace Cortexa.JobOrchestrator.Application.Models;

public sealed record CreateBatchCommand(
    IReadOnlyList<(string Filename, Stream Content, string ContentType)> Files,
    string BatchName,
    string Engine,
    string AiModel,
    string SeedCorpusDomain,
    string? RepoUrl = null,
    string? GitProvider = null,
    string? GitBranch = null,
    string? GitPatRaw = null,
    string? OrgId = null,
    string? UserId = null,
    IReadOnlyList<SavedRepositoryRef>? SavedRepositories = null);

public sealed record CreateBatchResponse(string BatchId, int DocumentCount, string Status);

public sealed record StartBatchResponse(string BatchId, string Status);

public sealed class BatchStatusResponse
{
    public string BatchId { get; init; } = string.Empty;
    public string? BatchName { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool WantsHarvesting { get; init; }
    public bool WantsSeeding { get; init; }
    public string? SeedingMode { get; init; }
    public string? CreatedAt { get; init; }
    public IReadOnlyList<BatchDocumentStatusDto> Documents { get; init; } = [];
    public IReadOnlyList<string> UnavailableSources { get; init; } = [];
}

public sealed class BatchDocumentStatusDto
{
    public string DocumentId { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public sealed class BatchSummaryDto
{
    public string BatchId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Status { get; init; } = string.Empty;
    public int DocumentCount { get; init; }
    public int CompletedCount { get; init; }
    public string? SeedingMode { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
}

public sealed class BatchResultsResponse
{
    public HarvestingResultRecord? Harvesting { get; init; }
    public SeedingReportRecord? Seeding { get; init; }
}

public sealed record StopBatchRequest(string? Reason);

public sealed record StopBatchResponse(string BatchId, string Status);
