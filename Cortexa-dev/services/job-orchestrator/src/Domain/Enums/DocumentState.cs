namespace Cortexa.JobOrchestrator.Domain.Enums;

public enum DocumentState
{
    Queued,
    Ingested,
    Extracted,
    Scored,
    Harvested,
    Seeded,
    Complete,
    Failed,
    Cancelled,
    NoCandidates
}
