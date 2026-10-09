using Collector.Domain.Enums;

namespace Collector.Infrastructure.Options;

public sealed class AiModelChoiceOptions
{
    public const string SectionName = "AiModelChoice";

    public CollectorProvider? Provider { get; set; }

    public string? Model { get; set; }
}

public sealed class GeminiRotationOptions
{
    public const string SectionName = "GeminiRotation";

    public string? ActiveKeyId { get; set; }
}
