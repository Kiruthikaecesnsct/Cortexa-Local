namespace Collector.Application.Candidates;

public sealed record WholeDocument(string DocumentId, string SourcePath, string Filename, bool IsCode, string Text);

public sealed record WholeDocumentRead
{
    public WholeDocument? Document { get; private init; }

    public CandidateOutcome Outcome { get; private init; } = CandidateOutcome.Completed;

    public string? Reason { get; private init; }

    public long? SizeBytes { get; private init; }

    public long? CapBytes { get; private init; }

    public bool IsRead => Document is not null;

    public static WholeDocumentRead Read(WholeDocument document) => new() { Document = document };

    public static WholeDocumentRead Rejected(CandidateOutcome outcome, string reason, long? sizeBytes = null, long? capBytes = null) =>
        new() { Outcome = outcome, Reason = reason, SizeBytes = sizeBytes, CapBytes = capBytes };
}
