using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Domain.Entities;

public sealed class BatchSaga
{
    public string Id { get; init; }
    public BatchState State { get; set; }
    public List<DocumentProgress> Documents { get; init; }
    public bool WantsHarvesting { get; init; }
    public bool WantsSeeding { get; init; }
    public int Version { get; set; }
    public string? ETag { get; set; }
    public int SchemaVersion { get; init; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public HashSet<string> ActiveDocumentIds { get; init; }
    public Queue<string> QueuedDocumentIds { get; init; }

    public int ActiveCount => ActiveDocumentIds.Count;
    public int CompletedCount => Documents.Count(d => d.State is DocumentState.Complete or DocumentState.Failed or DocumentState.NoCandidates);

    public BatchMetadata? Metadata { get; init; }

    public bool IsDeepSeeding =>
        string.Equals(Metadata?.SeedingMode, SeedingModes.Deep, StringComparison.OrdinalIgnoreCase);

    public int EvidenceCompletedCount { get; private set; }
    public Dictionary<string, int> EvidenceSourceLiveCounts { get; init; }
    public HashSet<string> CountedEvidenceCandidateIds { get; init; }

    public int ExpectedAssetEmbeddingUnits { get; private set; }
    public int CompletedAssetEmbeddingUnits { get; private set; }
    public HashSet<string> RecordedAssetEmbeddingUnitKeys { get; init; }

    public HashSet<string> RecordedDigestRequestedDocumentIds { get; init; }
    public HashSet<string> RecordedDigestCompletedDocumentIds { get; init; }

    public HashSet<string> RecordedLandscapeRequestedDocumentIds { get; init; }
    public HashSet<string> RecordedLandscapeCompletedDocumentIds { get; init; }

    public BatchSaga(
        string id,
        BatchState state,
        List<DocumentProgress> documents,
        bool wantsHarvesting,
        bool wantsSeeding,
        int version,
        string? eTag,
        int schemaVersion,
        string? failureReason = null,
        DateTimeOffset? failedAt = null,
        HashSet<string>? activeDocumentIds = null,
        Queue<string>? queuedDocumentIds = null,
        BatchMetadata? metadata = null,
        DateTimeOffset? cancelledAt = null,
        int evidenceCompletedCount = 0,
        Dictionary<string, int>? evidenceSourceLiveCounts = null,
        HashSet<string>? countedEvidenceCandidateIds = null,
        int expectedAssetEmbeddingUnits = 0,
        int completedAssetEmbeddingUnits = 0,
        HashSet<string>? recordedAssetEmbeddingUnitKeys = null,
        HashSet<string>? recordedDigestRequestedDocumentIds = null,
        HashSet<string>? recordedDigestCompletedDocumentIds = null,
        HashSet<string>? recordedLandscapeRequestedDocumentIds = null,
        HashSet<string>? recordedLandscapeCompletedDocumentIds = null)
    {
        Id = id;
        State = state;
        Documents = documents;
        WantsHarvesting = wantsHarvesting;
        WantsSeeding = wantsSeeding;
        Version = version;
        ETag = eTag;
        SchemaVersion = schemaVersion;
        FailureReason = failureReason;
        FailedAt = failedAt;
        ActiveDocumentIds = activeDocumentIds ?? [];
        QueuedDocumentIds = queuedDocumentIds ?? new Queue<string>();
        Metadata = metadata;
        CancelledAt = cancelledAt;
        EvidenceCompletedCount = evidenceCompletedCount;
        EvidenceSourceLiveCounts = evidenceSourceLiveCounts ?? [];
        CountedEvidenceCandidateIds = countedEvidenceCandidateIds ?? [];
        ExpectedAssetEmbeddingUnits = expectedAssetEmbeddingUnits;
        CompletedAssetEmbeddingUnits = completedAssetEmbeddingUnits;
        RecordedAssetEmbeddingUnitKeys = recordedAssetEmbeddingUnitKeys ?? [];
        RecordedDigestRequestedDocumentIds = recordedDigestRequestedDocumentIds ?? [];
        RecordedDigestCompletedDocumentIds = recordedDigestCompletedDocumentIds ?? [];
        RecordedLandscapeRequestedDocumentIds = recordedLandscapeRequestedDocumentIds ?? [];
        RecordedLandscapeCompletedDocumentIds = recordedLandscapeCompletedDocumentIds ?? [];
    }

    public void MarkFailed(string reason, DateTimeOffset at)
    {
        State = BatchState.Failed;
        FailureReason = reason;
        FailedAt = at;
        IncrementVersion();
    }

    public void MarkCancelled(string reason, DateTimeOffset at)
    {
        State = BatchState.Cancelled;
        FailureReason = reason;
        CancelledAt = at;

        foreach (var doc in Documents.Where(d => !d.IsTerminal))
            doc.State = DocumentState.Cancelled;

        ActiveDocumentIds.Clear();
        QueuedDocumentIds.Clear();
        IncrementVersion();
    }

    public DocumentProgress? FindDocument(string documentId)
    {
        return Documents.FirstOrDefault(d => d.DocumentId == documentId);
    }

    public void AddDocument(DocumentProgress document)
    {
        Documents.Add(document);
    }

    public void IncrementVersion()
    {
        Version++;
    }

    public void SeedFanOut(IReadOnlyList<string> documentIds, int cap)
    {
        if (ActiveDocumentIds.Count > 0 || QueuedDocumentIds.Count > 0)
            return;

        var active = documentIds.Take(cap).ToList();
        var queued = documentIds.Skip(cap).ToList();

        foreach (var id in active)
        {
            ActiveDocumentIds.Add(id);
            EnsureDocumentExists(id);
        }

        foreach (var id in queued)
            QueuedDocumentIds.Enqueue(id);

        if (State == BatchState.Queued)
            State = BatchState.InProgress;

        IncrementVersion();
    }

    public void MarkActiveTerminal(string documentId)
    {
        ActiveDocumentIds.Remove(documentId);
    }

    public string? TryReleaseNext()
    {
        if (QueuedDocumentIds.Count == 0)
            return null;

        var released = QueuedDocumentIds.Dequeue();
        ActiveDocumentIds.Add(released);
        EnsureDocumentExists(released);
        return released;
    }

    private void EnsureDocumentExists(string documentId)
    {
        if (FindDocument(documentId) is null)
            AddDocument(new DocumentProgress(documentId, DocumentState.Queued));
    }

    // Idempotent per-candidate wrapper. Service Bus delivers evidence.completed
    // at-least-once, so the same candidate can arrive more than once; counting it
    // twice would inflate the live ratio and hide a real source outage. Returns
    // false when the candidate was already counted.
    public bool TryRecordEvidenceCoverage(string candidateId, bool hasPatentApi, bool hasCorpus, bool hasLlm)
    {
        if (!CountedEvidenceCandidateIds.Add(candidateId))
            return false;

        RecordEvidenceCoverage(hasPatentApi, hasCorpus, hasLlm);
        return true;
    }

    public void RecordEvidenceCoverage(bool hasPatentApi, bool hasCorpus, bool hasLlm)
    {
        EvidenceCompletedCount++;

        if (hasPatentApi)
            IncrementSourceLiveCount("PatentApi");

        if (hasCorpus)
            IncrementSourceLiveCount("SeedCorpus");

        if (hasLlm)
            IncrementSourceLiveCount("LlmResearch");
    }

    private void IncrementSourceLiveCount(string sourceName)
    {
        if (!EvidenceSourceLiveCounts.TryGetValue(sourceName, out var current))
            current = 0;

        EvidenceSourceLiveCounts[sourceName] = current + 1;
    }

    public void AddExpectedAssetEmbeddingUnits(int count)
    {
        if (count > 0)
            ExpectedAssetEmbeddingUnits += count;
    }

    public bool TryRecordAssetEmbeddingUnit(string unitKey)
    {
        if (string.IsNullOrWhiteSpace(unitKey))
            return false;

        if (!RecordedAssetEmbeddingUnitKeys.Add(unitKey))
            return false;

        CompletedAssetEmbeddingUnits++;
        return true;
    }

    public bool AllAssetEmbeddingUnitsRecordedFor(string documentId, int unitCount)
    {
        if (string.IsNullOrWhiteSpace(documentId) || unitCount <= 0)
            return false;

        var prefix = $"{documentId}:";
        var recorded = RecordedAssetEmbeddingUnitKeys.Count(key => key.StartsWith(prefix, StringComparison.Ordinal));
        return recorded >= unitCount;
    }

    public bool TryRecordDigestRequested(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return RecordedDigestRequestedDocumentIds.Add(documentId);
    }

    public bool TryRecordDigestCompleted(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return RecordedDigestCompletedDocumentIds.Add(documentId);
    }

    public bool TryRecordLandscapeRequested(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return RecordedLandscapeRequestedDocumentIds.Add(documentId);
    }

    public bool TryRecordLandscapeCompleted(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return RecordedLandscapeCompletedDocumentIds.Add(documentId);
    }
}
