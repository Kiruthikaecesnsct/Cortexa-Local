namespace Collector.Application.Candidates;

public enum CandidateOutcome
{
    Completed,
    TooLarge,
    KeyMissing,
    KeyRejected,
    Quota,
    Network,
    Canceled,
    Failed,
}
