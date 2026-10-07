using Collector.Domain.Enums;

namespace Collector.Infrastructure.Options;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public CollectorProvider Provider { get; set; } = CollectorProvider.Claude;
}
