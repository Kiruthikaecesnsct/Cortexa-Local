namespace Cortexa.JobOrchestrator.Application.Models;

public sealed record DocumentRecord(
    string DocumentId,
    string BatchId,
    string Filename,
    string BlobUri)
{
    public string Status { get; init; } = "queued";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? SourceKind { get; init; }
}
