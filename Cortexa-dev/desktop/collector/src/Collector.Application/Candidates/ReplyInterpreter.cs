using Collector.Application.Ports;
using Collector.Domain.Documents;

namespace Collector.Application.Candidates;

public sealed class ReplyInterpreter(CandidateParser parser, ExcerptVerifier verifier)
{
    private const string ParseFailureReason = "output malformed";

    public static DocumentRun Fail(CollectorDocument document, CandidateFailure failure) =>
        new(CandidateDocumentResult.Failure(document.Id, document.Filename, failure.Outcome, failure.Reason), null);

    public DocumentRun Interpret(CollectorDocument document, PreparedDocument prepared, AiCompletion completion)
    {
        var failure = CandidateOutcomeMapper.FromCompletion(completion.Outcome);
        if (failure is not null)
        {
            return Fail(document, failure);
        }

        var parsed = parser.Parse(completion.Text, prepared.Whole.SourcePath);
        if (!parsed.IsSuccess)
        {
            return Fail(document, new CandidateFailure(CandidateOutcome.Failed, parsed.Failure ?? ParseFailureReason));
        }

        var candidates = verifier.Verify(prepared.Whole, parsed.Candidates!);
        var result = CandidateDocumentResult.Failure(document.Id, document.Filename, CandidateOutcome.Completed, null)
            with
        { TokenCount = prepared.Limit.TokenCount, TokenLimit = prepared.Limit.Limit, Candidates = candidates };
        return new DocumentRun(result, completion.Model);
    }
}
