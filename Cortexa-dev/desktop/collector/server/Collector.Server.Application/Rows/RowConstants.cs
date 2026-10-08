namespace Collector.Server.Application.Rows;

public static class RowConstants
{
    public const string DocumentStatusCompleted = "completed";
    public const string SagaStateInProgress = "InProgress";
    public const string SagaDocumentStateQueued = "Queued";
    public const string SagaDocumentStateIngested = "Ingested";
    public const string SagaDocumentStateExtracted = "Extracted";
    public const string SagaDocumentStateScored = "Scored";
    public const string SagaDocumentStateHarvested = "Harvested";
    public const string SagaDocumentStateSeeded = "Seeded";
    public const string SagaDocumentStateComplete = "Complete";
    public const string SagaDocumentStateNoCandidates = "NoCandidates";
    public const string SagaDocumentStateFailed = "Failed";
    public const string SagaDocumentStateCancelled = "Cancelled";
    public const string PatentabilityAxis = "Patentability";
    public const string HarvestingEngine = "harvesting";
    public const string SeedingEngine = "seeding";
    public const int SagaSchemaVersion = 1;
    public const int InitialSagaVersion = 0;
}
