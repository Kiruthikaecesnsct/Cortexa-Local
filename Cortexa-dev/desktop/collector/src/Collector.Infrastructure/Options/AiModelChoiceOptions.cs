using Collector.Domain.Enums;

namespace Collector.Infrastructure.Options;

public sealed class AiModelChoiceOptions
{
    public const string SectionName = "AiModelChoice";

    public CollectorProvider? Provider { get; set; }

    public string? Model { get; set; }
}
