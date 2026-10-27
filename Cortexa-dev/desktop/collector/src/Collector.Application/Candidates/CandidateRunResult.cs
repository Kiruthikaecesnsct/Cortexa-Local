namespace Collector.Application.Candidates;

public sealed record CandidateDocumentResult(
    string DocumentId,
    string Filename,
    CandidateOutcome Outcome,
    string? Reason,
    int? TokenCount,
    int? TokenLimit,
    long? SizeBytes,
    long? SizeCapBytes,
    IReadOnlyList<InventionCandidate> Candidates)
{
    public static CandidateDocumentResult Failure(string documentId, string filename, CandidateOutcome outcome, string? reason) =>
        new(documentId, filename, outcome, reason, null, null, null, null, []);

    public static CandidateDocumentResult Canceled(string documentId, string filename) =>
        Failure(documentId, filename, CandidateOutcome.Canceled, null);
}

public sealed record CandidateRunResult(IReadOnlyList<CandidateDocumentResult> Documents, string Model)
{
    public IReadOnlyList<InventionCandidate> AllCandidates => [.. Documents.SelectMany(document => document.Candidates)];
}

public sealed record CandidateRunRequest(IReadOnlyList<string> DocumentIds)
{
    public IReadOnlyList<string> DistinctDocumentIds { get; } =
        [.. DocumentIds.Distinct(StringComparer.Ordinal)];
}

public sealed record CandidateProgress(int Completed, int Total);
