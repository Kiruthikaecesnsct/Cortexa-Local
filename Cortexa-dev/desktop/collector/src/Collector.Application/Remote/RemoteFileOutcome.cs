namespace Collector.Application.Remote;

public enum RemoteFileOutcomeKind
{
    Downloaded,
    CacheHit,
    TooLarge,
    Skipped,
}

public sealed record RemoteFileOutcome(RemoteFileOutcomeKind Kind, string? LocalPath)
{
    public static RemoteFileOutcome Downloaded(string localPath) => new(RemoteFileOutcomeKind.Downloaded, localPath);

    public static RemoteFileOutcome CacheHit(string localPath) => new(RemoteFileOutcomeKind.CacheHit, localPath);

    public static RemoteFileOutcome TooLarge { get; } = new(RemoteFileOutcomeKind.TooLarge, null);

    public static RemoteFileOutcome Skipped { get; } = new(RemoteFileOutcomeKind.Skipped, null);
}
