namespace Collector.Application.Knowledge;

public sealed record KnowledgePrompt
{
    public required string Version { get; init; }

    public required string SystemText { get; init; }

    public required string SchemaJson { get; init; }

    public required GuardText Guard { get; init; }
}
