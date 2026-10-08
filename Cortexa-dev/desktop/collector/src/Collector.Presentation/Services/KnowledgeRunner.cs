using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public sealed class KnowledgeRunner(
    ExtractKnowledgeHandler handler,
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
            return new KnowledgeRunOutcome(result.IsFailed ? KnowledgeRunStatus.Failed : KnowledgeRunStatus.Completed, result);
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
}
