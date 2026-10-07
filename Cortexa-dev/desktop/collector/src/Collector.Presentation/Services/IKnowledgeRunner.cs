using Collector.Application.Knowledge;
using Collector.Domain.Enums;

namespace Collector.Presentation.Services;

public enum KnowledgeRunStatus
{
    Completed,
    Canceled,
    KeyMissing,
    Failed,
}

public sealed record KnowledgeRunOutcome(
    KnowledgeRunStatus Status,
    ExtractionRunResult? Result = null,
    CollectorProvider Provider = CollectorProvider.Claude);

public interface IKnowledgeRunner
{
    Task<KnowledgeRunOutcome> RunAsync(
        IReadOnlyList<string> documentIds,
        IProgress<ExtractionProgress> progress,
        CancellationToken cancellationToken);
}
