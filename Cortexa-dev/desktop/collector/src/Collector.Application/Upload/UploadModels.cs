using Collector.Application.Knowledge;
using Collector.Domain.Enums;

namespace Collector.Application.Upload;

public sealed record UploadRequest
{
    public required IReadOnlyList<ExtractedKnowledgeItem> Items { get; init; }

    public required CollectorProvider Provider { get; init; }

    public required string Model { get; init; }

    public required string PromptVersion { get; init; }

    public required string AppVersion { get; init; }

    public string? BatchName { get; init; }
}

public enum UploadBatchStatus
{
    Pending,
    Uploaded,
    Failed,
}

public enum UploadErrorKind
{
    Session,
    Rejected,
    Network,
    Unknown,
    InProgress,
}

public sealed record UploadError(UploadErrorKind Kind, string? RejectedCode)
{
    public bool CanRetry => Kind != UploadErrorKind.Rejected;
}

public sealed record BatchUploadResult
{
    public required int Index { get; init; }

    public required int DocumentCount { get; init; }

    public required int ItemCount { get; init; }

    public required UploadBatchStatus Status { get; init; }

    public string? ServerBatchId { get; init; }

    public UploadError? Error { get; init; }
}
