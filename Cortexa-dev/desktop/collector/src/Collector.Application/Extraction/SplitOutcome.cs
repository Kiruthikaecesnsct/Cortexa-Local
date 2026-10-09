namespace Collector.Application.Extraction;

public sealed record SplitOutcome(
    ExtractionResult Result,
    string? RepoPath,
    int UnitCount,
    int TokenCount,
    int PromptTokens);
