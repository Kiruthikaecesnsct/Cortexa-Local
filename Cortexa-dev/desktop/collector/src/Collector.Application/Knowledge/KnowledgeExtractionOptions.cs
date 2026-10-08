using Collector.Domain.Enums;

namespace Collector.Application.Knowledge;

public sealed class KnowledgeExtractionOptions
{
    public CollectorProvider Provider { get; set; } = CollectorProvider.Claude;
}
