using System.Net;
using Collector.Application.Ports;

namespace Collector.Application.Candidates;

public sealed record CandidateFailure(CandidateOutcome Outcome, string Reason);

public static class CandidateOutcomeMapper
{
    private const string RefusedReason = "refused";
    private const string TruncatedReason = "output truncated";

    public static CandidateFailure FromException(AiProviderException exception) => exception.Kind switch
    {
        AiFailureKind.MissingApiKey => new CandidateFailure(CandidateOutcome.KeyMissing, exception.Message),
        AiFailureKind.QuotaExceeded => new CandidateFailure(CandidateOutcome.Quota, exception.Message),
        AiFailureKind.Transient => new CandidateFailure(CandidateOutcome.Network, exception.Message),
        _ => FromPermanent(exception),
    };

    public static CandidateFailure? FromCompletion(AiOutcome outcome) => outcome switch
    {
        AiOutcome.Refused => new CandidateFailure(CandidateOutcome.Failed, RefusedReason),
        AiOutcome.Truncated => new CandidateFailure(CandidateOutcome.Failed, TruncatedReason),
        _ => null,
    };

    private static CandidateFailure FromPermanent(AiProviderException exception) =>
        exception.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden
            ? new CandidateFailure(CandidateOutcome.KeyRejected, exception.Message)
            : new CandidateFailure(CandidateOutcome.Failed, exception.Message);
}
