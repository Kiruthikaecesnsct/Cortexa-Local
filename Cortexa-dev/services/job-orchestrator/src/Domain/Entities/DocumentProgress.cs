using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Domain.Entities;

public sealed class DocumentProgress
{
    public string DocumentId { get; init; }
    public DocumentState State { get; set; }
    public string? FailureReason { get; set; }
    public int ExpectedCandidateCount { get; set; }
    public HashSet<string> CompletedCandidateIds { get; init; }
    public HashSet<string> FailedCandidateIds { get; init; }
    public int ExpectedExtractionUnits { get; set; }
    public HashSet<int> ReceivedExtractionUnitIndices { get; init; }

    public HashSet<string> SeededCandidateIds { get; init; }
    public HashSet<string> CompletedSeededCandidateIds { get; init; }
    public HashSet<string> FailedSeededCandidateIds { get; init; }

    public bool IsTerminal => State is DocumentState.Complete or DocumentState.Failed or DocumentState.Cancelled or DocumentState.NoCandidates;

    public bool AllCandidatesResolved => ExpectedCandidateCount > 0 && ResolvedCount >= ExpectedCandidateCount;

    public bool HasAnySuccessfulCandidate => CompletedCandidateIds.Count > 0;

    public bool AllExtractionUnitsReceived =>
        ExpectedExtractionUnits > 0 && ReceivedExtractionUnitIndices.Count >= ExpectedExtractionUnits;

    public bool AllSeededResolved =>
        SeededCandidateIds.Count > 0 && ResolvedSeededCount >= SeededCandidateIds.Count;

    public bool HasAnySeededSuccess => CompletedSeededCandidateIds.Count > 0;

    private int ResolvedCount => CompletedCandidateIds.Count + FailedCandidateIds.Count;

    private int ResolvedSeededCount => CompletedSeededCandidateIds.Count + FailedSeededCandidateIds.Count;

    public DocumentProgress(string documentId, DocumentState state)
    {
        DocumentId = documentId;
        State = state;
        CompletedCandidateIds = [];
        FailedCandidateIds = [];
        ReceivedExtractionUnitIndices = [];
        SeededCandidateIds = [];
        CompletedSeededCandidateIds = [];
        FailedSeededCandidateIds = [];
    }

    public static DocumentProgress FromPersistence(DocumentProgressPersistenceData data)
    {
        return new DocumentProgress(data.DocumentId, data.State)
        {
            FailureReason = data.FailureReason,
            ExpectedCandidateCount = data.ExpectedCandidateCount,
            CompletedCandidateIds = new HashSet<string>(data.CompletedCandidateIds),
            FailedCandidateIds = new HashSet<string>(data.FailedCandidateIds),
            ExpectedExtractionUnits = data.ExpectedExtractionUnits,
            ReceivedExtractionUnitIndices = new HashSet<int>(data.ReceivedExtractionUnitIndices),
            SeededCandidateIds = new HashSet<string>(data.SeededCandidateIds),
            CompletedSeededCandidateIds = new HashSet<string>(data.CompletedSeededCandidateIds),
            FailedSeededCandidateIds = new HashSet<string>(data.FailedSeededCandidateIds)
        };
    }

    public void SetExpectedCandidates(int count)
    {
        ExpectedCandidateCount = count > 0 ? count : 0;
    }

    public void SetExpectedExtractionUnits(int count)
    {
        ExpectedExtractionUnits = count > 0 ? count : 1;
    }

    public void EnsureExpectedExtractionUnits(int count)
    {
        if (ExpectedExtractionUnits <= 0)
            SetExpectedExtractionUnits(count);
    }

    public bool RecordExtractionUnit(int unitIndex, IReadOnlyCollection<string> candidateIds)
    {
        EnsureExpectedExtractionUnits(1);

        if (!ReceivedExtractionUnitIndices.Add(unitIndex))
            return false;

        ExpectedCandidateCount += candidateIds.Count;
        return true;
    }

    public void MarkCandidateScored(string candidateId)
    {
        if (!string.IsNullOrWhiteSpace(candidateId))
            CompletedCandidateIds.Add(candidateId);
    }

    public void MarkCandidateFailed(string candidateId)
    {
        if (!string.IsNullOrWhiteSpace(candidateId))
            FailedCandidateIds.Add(candidateId);
    }

    public bool IsSeededCandidate(string candidateId) =>
        !string.IsNullOrWhiteSpace(candidateId) && SeededCandidateIds.Contains(candidateId);

    public bool TryRecordSeededCandidates(IReadOnlyCollection<string> candidateIds)
    {
        if (SeededCandidateIds.Count > 0)
            return false;

        foreach (var candidateId in candidateIds)
        {
            if (!string.IsNullOrWhiteSpace(candidateId))
                SeededCandidateIds.Add(candidateId);
        }

        return SeededCandidateIds.Count > 0;
    }

    public void MarkSeededScored(string candidateId)
    {
        if (!string.IsNullOrWhiteSpace(candidateId))
            CompletedSeededCandidateIds.Add(candidateId);
    }

    public void MarkSeededFailed(string candidateId)
    {
        if (!string.IsNullOrWhiteSpace(candidateId))
            FailedSeededCandidateIds.Add(candidateId);
    }
}

public sealed record DocumentProgressPersistenceData(
    string DocumentId,
    DocumentState State,
    string? FailureReason,
    int ExpectedCandidateCount,
    IEnumerable<string> CompletedCandidateIds,
    IEnumerable<string> FailedCandidateIds,
    int ExpectedExtractionUnits,
    IEnumerable<int> ReceivedExtractionUnitIndices,
    IEnumerable<string> SeededCandidateIds,
    IEnumerable<string> CompletedSeededCandidateIds,
    IEnumerable<string> FailedSeededCandidateIds);
