using Collector.Application.Candidates;
using Collector.Application.Settings;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public sealed class CandidateRunner(
    IdentifyCandidatesHandler handler,
    SettingsService settings,
    ILogger<CandidateRunner> logger) : ICandidateRunner
{
    public async Task<CandidateRunOutcome> RunAsync(
        IReadOnlyList<string> documentIds,
        IProgress<CandidateProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.IdentifyAsync(new CandidateRunRequest(documentIds), progress, cancellationToken);
            var keyCount = await ConfiguredKeyCountAsync(result, cancellationToken);
            return new CandidateRunOutcome(result, cancellationToken.IsCancellationRequested, keyCount);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Candidate identification failed unexpectedly ({ExceptionType}).", exception.GetType().Name);
            return CandidateRunOutcome.Failed();
        }
    }

    private async Task<int> ConfiguredKeyCountAsync(CandidateRunResult result, CancellationToken cancellationToken)
    {
        if (!result.Documents.Any(IsKeyRelated))
        {
            return 0;
        }

        var keys = await settings.GetGeminiKeySummariesAsync(cancellationToken);
        return keys.Count;
    }

    private static bool IsKeyRelated(CandidateDocumentResult document) =>
        document.Outcome is CandidateOutcome.KeyRejected or CandidateOutcome.Quota;
}
