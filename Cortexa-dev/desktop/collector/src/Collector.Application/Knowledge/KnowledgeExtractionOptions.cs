using Collector.Domain.Enums;

namespace Collector.Application.Knowledge;

public sealed class KnowledgeExtractionOptions
{
    public int Concurrency { get; set; } = 4;

    public CollectorProvider Provider { get; set; } = CollectorProvider.Claude;
}
