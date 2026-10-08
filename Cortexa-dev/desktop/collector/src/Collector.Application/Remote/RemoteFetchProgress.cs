namespace Collector.Application.Remote;

public enum RemoteFetchPhase
{
    ReadingTree,
    Downloading,
    Completed,
}

public sealed record RemoteFetchProgress(RemoteFetchPhase Phase, int Processed, int Total);
