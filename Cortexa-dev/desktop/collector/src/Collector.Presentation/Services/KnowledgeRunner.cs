using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.Services;

public sealed class KnowledgeRunner(
    ExtractKnowledgeHandler handler,
    IOptions<KnowledgeExtractionOptions> options,
    ILogger<KnowledgeRunner> logger) : IKnowledgeRunner
{
    public async Task<KnowledgeRunOutcome> RunAsync(
        IReadOnlyList<string> documentIds,
        IProgress<ExtractionProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.ExtractAsync(new ExtractionRunRequest(documentIds), progress, cancellationToken);
            return new KnowledgeRunOutcome(result.IsFailed ? KnowledgeRunStatus.Failed : KnowledgeRunStatus.Completed, result);
        }
        catch (OperationCanceledException)
        {
            return new KnowledgeRunOutcome(KnowledgeRunStatus.Canceled);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.MissingApiKey)
        {
            return new KnowledgeRunOutcome(KnowledgeRunStatus.KeyMissing, Provider: options.Value.Provider);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Knowledge extraction failed unexpectedly.");
            return new KnowledgeRunOutcome(KnowledgeRunStatus.Failed);
        }
    }
}
