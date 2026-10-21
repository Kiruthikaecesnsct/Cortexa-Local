using Collector.Application.Candidates;
using Collector.Presentation.Services;

namespace Collector.Tests.Presentation;

internal static class CandidateData
{
    public const string Model = "gemini-test";
    public const string CandidateTitle = "Adaptive cache";
    public const string CandidateSummary = "Evicts by predicted reuse distance";

    public static InventionCandidate Candidate(string title = CandidateTitle, string file = "a.txt") =>
        new(title, CandidateSummary, "Novel eviction rule", file, new CandidateExcerpt("quote", true, 0, 5));

    public static CandidateDocumentResult Doc(CandidateOutcome outcome, string name = "a.txt", params InventionCandidate[] candidates) =>
        new($"id-{name}", name, outcome, null, null, null, null, null, candidates);

    public static CandidateDocumentResult Completed(string name = "a.txt", string title = CandidateTitle) =>
        Doc(CandidateOutcome.Completed, name, Candidate(title, name));

    public static CandidateDocumentResult TooLargeTokens(string name, int tokens = 2000, int limit = 1000) =>
        new($"id-{name}", name, CandidateOutcome.TooLarge, null, tokens, limit, null, null, []);

    public static CandidateDocumentResult TooLargeSize(string name, long size, long cap) =>
        new($"id-{name}", name, CandidateOutcome.TooLarge, null, null, null, size, cap, []);

    public static CandidateRunResult Result(params CandidateDocumentResult[] documents) => new(documents, Model);

    public static CandidateRunOutcome Outcome(CandidateRunResult result, bool canceled = false, int keys = 0) =>
        new(result, canceled, keys);

    public static CandidateRunState StateFor(CandidateRunOutcome outcome)
    {
        var state = new CandidateRunState();
        state.Set(outcome);
        return state;
    }
}

internal sealed class FakeCandidateRunner : ICandidateRunner
{
    public CandidateRunOutcome Outcome { get; set; } = CandidateData.Outcome(CandidateData.Result());

    public TaskCompletionSource<CandidateRunOutcome>? Gate { get; set; }

    public CandidateRunOutcome? CanceledOutcome { get; set; }

    public IReadOnlyList<string> LastDocumentIds { get; private set; } = [];

    public int Calls { get; private set; }

    public Task<CandidateRunOutcome> RunAsync(
        IReadOnlyList<string> documentIds,
        IProgress<CandidateProgress> progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastDocumentIds = documentIds;
        var gate = Gate;
        if (gate is null)
        {
            return Task.FromResult(Outcome);
        }

        cancellationToken.Register(() => gate.TrySetResult(CanceledOutcome ?? CandidateRunOutcome.Failed()));
        return gate.Task;
    }
}
