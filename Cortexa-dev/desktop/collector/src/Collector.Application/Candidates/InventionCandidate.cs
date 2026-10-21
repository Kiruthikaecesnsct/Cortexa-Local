namespace Collector.Application.Candidates;

public sealed record CandidateExcerpt(string Quote, bool Verified, int? Offset, int? Length);

public sealed record InventionCandidate(
    string Title,
    string Summary,
    string PatentabilityRationale,
    string SourceFile,
    CandidateExcerpt? Excerpt);
