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

public sealed record KnowledgeRunRequest(
    IReadOnlyList<string> DocumentIds,
    CollectorProvider Provider = CollectorProvider.Claude,
    string? Model = null);

public interface IKnowledgeRunner
{
    Task<KnowledgeRunOutcome> RunAsync(
        KnowledgeRunRequest request,
        IProgress<ExtractionProgress> progress,
        CancellationToken cancellationToken);
}
