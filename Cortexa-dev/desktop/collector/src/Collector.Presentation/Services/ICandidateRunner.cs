using Collector.Application.Candidates;

namespace Collector.Presentation.Services;

public sealed record CandidateRunOutcome(CandidateRunResult? Result, bool Canceled, int ConfiguredKeyCount)
{
    public bool IsFailed => Result is null;

    public static CandidateRunOutcome Failed() => new(null, false, 0);
}

public interface ICandidateRunner
{
    Task<CandidateRunOutcome> RunAsync(
        IReadOnlyList<string> documentIds,
        IProgress<CandidateProgress> progress,
        CancellationToken cancellationToken);
}
