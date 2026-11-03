using Collector.Domain.Enums;

namespace Collector.Infrastructure.Options;

public sealed class AiInputLimitOptions
{
    public const string SectionName = "Ai:InputLimits";

    public Dictionary<CollectorProvider, int> MaxInputTokens { get; set; } = [];
}
