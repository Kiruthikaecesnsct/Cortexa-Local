using Collector.Domain.Enums;

namespace Collector.Application.Settings;

public sealed record AiModelChoice(CollectorProvider Provider, string Model);
