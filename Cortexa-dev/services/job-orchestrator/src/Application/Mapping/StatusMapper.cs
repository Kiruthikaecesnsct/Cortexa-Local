using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Mapping;

public static class StatusMapper
{
    public static string MapBatchStatus(BatchSaga saga) => saga.State switch
    {
        BatchState.Queued => "Running",
        BatchState.InProgress => "Running",
        BatchState.Completed when saga.Documents.Any(d => d.State == DocumentState.Failed) => "PartiallyFailed",
        BatchState.Completed => "Completed",
        BatchState.Failed => "Failed",
        BatchState.Cancelled => "Cancelled",
        _ => "Running"
    };

    public static string MapDocumentStatus(DocumentState state) => state switch
    {
        DocumentState.Queued => "Ingesting",
        DocumentState.Ingested => "Ingested",
        DocumentState.Extracted => "Extracted",
        DocumentState.Scored => "Scored",
        DocumentState.Harvested => "EngineDone",
        DocumentState.Seeded => "EngineDone",
        DocumentState.Complete => "EngineDone",
        DocumentState.Failed => "Failed",
        DocumentState.Cancelled => "Cancelled",
        DocumentState.NoCandidates => "NoCandidates",
        _ => "Ingesting"
    };

    public static IReadOnlyList<string> ComputeUnavailableSources(BatchSaga saga, double outageThreshold)
    {
        if (saga.EvidenceCompletedCount == 0)
            return [];

        var unavailable = new List<string>();

        foreach (var sourceName in new[] { "PatentApi", "SeedCorpus", "LlmResearch" })
        {
            var liveCount = saga.EvidenceSourceLiveCounts.GetValueOrDefault(sourceName, 0);
            if (IsSourceUnavailable(liveCount, saga.EvidenceCompletedCount, outageThreshold))
                unavailable.Add(sourceName);
        }

        return unavailable;
    }

    // A source is unavailable when it was live for zero candidates (a total batch
    // outage — always actionable), or its live ratio falls strictly below the
    // configured threshold. Keeping the zero-live case explicit lets the default
    // threshold of 0.0 mean "flag only total outages".
    private static bool IsSourceUnavailable(int liveCount, int completedCount, double outageThreshold)
    {
        if (liveCount == 0)
            return true;

        var ratio = (double)liveCount / completedCount;
        return ratio < outageThreshold;
    }
}
