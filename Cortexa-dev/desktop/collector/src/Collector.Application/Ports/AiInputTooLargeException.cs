namespace Collector.Application.Ports;

public sealed class AiInputTooLargeException(int tokenCount, int limit, string model)
    : AiProviderException(
        AiFailureKind.Permanent,
        $"The input has {tokenCount} tokens, over the {limit} token limit of model {model}.")
{
    public int TokenCount { get; } = tokenCount;

    public int Limit { get; } = limit;

    public string Model { get; } = model;
}
