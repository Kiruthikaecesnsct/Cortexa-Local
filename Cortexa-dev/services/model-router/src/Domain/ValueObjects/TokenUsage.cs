namespace Cortexa.ModelRouter.Domain.ValueObjects;

public sealed record TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);
