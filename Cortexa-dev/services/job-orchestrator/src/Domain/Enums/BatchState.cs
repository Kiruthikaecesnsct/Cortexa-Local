namespace Cortexa.JobOrchestrator.Domain.Enums;

public enum BatchState
{
    Queued,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
