namespace Collector.Application.Candidates;

public sealed record CandidatePrompt
{
    public required string Version { get; init; }

    public required string SystemText { get; init; }

    public required string SchemaJson { get; init; }
}
