using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public sealed class KnowledgeRunner(
    ExtractKnowledgeHandler handler,
    SettingsService settings,
    ILogger<KnowledgeRunner> logger) : IKnowledgeRunner
{
    public async Task<KnowledgeRunOutcome> RunAsync(
        KnowledgeRunRequest request,
        IProgress<ExtractionProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.ExtractAsync(
                new ExtractionRunRequest(request.DocumentIds, request.Provider, request.Model),
                progress,
                cancellationToken);
            return await BuildOutcomeAsync(request.Provider, result, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new KnowledgeRunOutcome(KnowledgeRunStatus.Canceled);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.MissingApiKey)
        {
            return new KnowledgeRunOutcome(KnowledgeRunStatus.KeyMissing, Provider: request.Provider);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Knowledge extraction failed unexpectedly.");
            return new KnowledgeRunOutcome(KnowledgeRunStatus.Failed);
        }
    }

    private async Task<KnowledgeRunOutcome> BuildOutcomeAsync(
        CollectorProvider provider,
        ExtractionRunResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsFailed)
        {
            return new KnowledgeRunOutcome(KnowledgeRunStatus.Completed, result, provider);
        }

        var status = StatusFor(result.FailureKind);
        var keyCount = await ConfiguredKeyCountAsync(provider, status, cancellationToken);
        return new KnowledgeRunOutcome(status, result, provider, keyCount);
    }

    private async Task<int> ConfiguredKeyCountAsync(
        CollectorProvider provider,
        KnowledgeRunStatus status,
        CancellationToken cancellationToken)
    {
        var isMultiKeyRelevant = provider == CollectorProvider.Gemini
            && status is KnowledgeRunStatus.KeyRejected or KnowledgeRunStatus.QuotaExceeded;
        if (!isMultiKeyRelevant)
        {
            return 0;
        }

        var keys = await settings.GetGeminiKeySummariesAsync(cancellationToken);
        return keys.Count;
    }

    private static KnowledgeRunStatus StatusFor(AiFailureKind? failureKind) => failureKind switch
    {
        AiFailureKind.Permanent => KnowledgeRunStatus.KeyRejected,
        AiFailureKind.QuotaExceeded => KnowledgeRunStatus.QuotaExceeded,
        AiFailureKind.Transient => KnowledgeRunStatus.NetworkFailed,
        _ => KnowledgeRunStatus.Failed,
    };
}
