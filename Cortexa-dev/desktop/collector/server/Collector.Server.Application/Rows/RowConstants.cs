namespace Collector.Server.Application.Rows;

public static class RowConstants
{
    public const string DocumentStatusCompleted = "completed";
    public const string SagaStateInProgress = "InProgress";
    public const string SagaDocumentStateQueued = "Queued";
    public const int SagaSchemaVersion = 1;
    public const int InitialSagaVersion = 0;
}
