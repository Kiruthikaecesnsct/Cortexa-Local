using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public sealed class RemoteSourceException : Exception
{
    public RemoteSourceException(
        RemoteFailureKind kind,
        SourceType provider,
        DateTimeOffset? resetAt = null,
        Exception? innerException = null)
        : base(RemoteFailureMessages.For(kind, provider), innerException)
    {
        Kind = kind;
        Provider = provider;
        ResetAt = resetAt;
    }

    public RemoteFailureKind Kind { get; }

    public SourceType Provider { get; }

    public DateTimeOffset? ResetAt { get; }
}
