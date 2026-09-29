namespace Cortexa.JobOrchestrator.Application.Models;

public sealed record DeleteBatchContext(
    string BatchId,
    DeletionRequestOptions Options,
    BatchAccess Access)
{
    public string Operator => Options.Operator;

    public string CorrelationId => Options.CorrelationId;

    public bool Force => Options.Force;
}

public sealed record DeletionRequestOptions(string Operator, string CorrelationId, bool Force);

public sealed record BatchAccess(string? CallerOrgId, bool IsSuperAdmin)
{
    public static BatchAccess Unrestricted => new(CallerOrgId: null, IsSuperAdmin: true);
}

public sealed record StoreDeletionResult(
    string StoreName,
    int DeletedCount,
    bool Success,
    string? Error = null);

public sealed record DeleteBatchResponse(
    string BatchId,
    string Status,
    bool FullyDeleted,
    IReadOnlyList<StoreDeletionResult> Stores);
