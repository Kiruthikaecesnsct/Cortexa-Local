using Collector.Application.Candidates;
using Collector.Application.Ports;

namespace Collector.Tests.Candidates;

public sealed class CandidateOutcomeMapperTests
{
    private const string Message = "provider said no";

    [Theory]
    [InlineData(AiFailureKind.MissingApiKey, CandidateOutcome.KeyMissing)]
    [InlineData(AiFailureKind.QuotaExceeded, CandidateOutcome.Quota)]
    [InlineData(AiFailureKind.Transient, CandidateOutcome.Network)]
    [InlineData(AiFailureKind.Permanent, CandidateOutcome.Failed)]
    public void FromException_MapsEveryKind(AiFailureKind kind, CandidateOutcome expected)
    {
        var failure = CandidateOutcomeMapper.FromException(new AiProviderException(kind, Message));

        Assert.Equal(expected, failure.Outcome);
        Assert.Equal(Message, failure.Reason);
    }

    [Theory]
    [InlineData(401, CandidateOutcome.KeyRejected)]
    [InlineData(403, CandidateOutcome.KeyRejected)]
    [InlineData(400, CandidateOutcome.Failed)]
    [InlineData(404, CandidateOutcome.Failed)]
    [InlineData(413, CandidateOutcome.Failed)]
    [InlineData(422, CandidateOutcome.Failed)]
    [InlineData(null, CandidateOutcome.Failed)]
    public void FromException_Permanent_UsesStatusCode(int? status, CandidateOutcome expected)
    {
        var failure = CandidateOutcomeMapper.FromException(
            new AiProviderException(AiFailureKind.Permanent, Message, null, status));

        Assert.Equal(expected, failure.Outcome);
    }

    [Fact]
    public void FromCompletion_Refused_FailedRefused()
    {
        var failure = CandidateOutcomeMapper.FromCompletion(AiOutcome.Refused);

        Assert.Equal(new CandidateFailure(CandidateOutcome.Failed, "refused"), failure);
    }

    [Fact]
    public void FromCompletion_Truncated_FailedTruncated()
    {
        var failure = CandidateOutcomeMapper.FromCompletion(AiOutcome.Truncated);

        Assert.Equal(new CandidateFailure(CandidateOutcome.Failed, "output truncated"), failure);
    }

    [Fact]
    public void FromCompletion_Completed_IsNull()
    {
        Assert.Null(CandidateOutcomeMapper.FromCompletion(AiOutcome.Completed));
    }
}
