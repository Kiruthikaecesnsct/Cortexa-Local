namespace Collector.Application.Ports;

public interface IAiProvider
{
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}

public sealed record AiRequest
{
    public required string SystemText { get; init; }

    public required string UserText { get; init; }

    public required string OutputSchemaJson { get; init; }

    public string? Model { get; init; }
}

public enum AiOutcome
{
    Completed,
    Truncated,
    Refused,
}

public sealed record AiCompletion
{
    public required string Text { get; init; }

    public required AiOutcome Outcome { get; init; }

    public required string Model { get; init; }
}

public enum AiFailureKind
{
    Transient,
    Permanent,
    MissingApiKey,
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(AiFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public AiFailureKind Kind { get; }

    public bool IsPermanent => Kind != AiFailureKind.Transient;
}
