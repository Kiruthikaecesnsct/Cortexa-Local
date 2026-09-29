namespace Cortexa.JobOrchestrator.Application.Constants;

public static class BatchScopedContainers
{
    public static readonly IReadOnlySet<string> DataContainersExcludingSaga = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "documents",
        "chunks",
        "provenance_maps",
        "candidates",
        "evidence_bundles",
        "verdicts",
        "reports",
        "harvesting",
        "seeding"
    };

    public const string SagaContainer = "batches";
}
