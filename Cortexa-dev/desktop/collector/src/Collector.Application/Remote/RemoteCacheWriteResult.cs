namespace Collector.Application.Remote;

public sealed record RemoteCacheWriteResult(long SizeBytes, bool ExceededLimit);
