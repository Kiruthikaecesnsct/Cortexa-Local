namespace Collector.Infrastructure.Remote;

internal static class RemoteRequestOptions
{
    public static readonly HttpRequestOptionsKey<bool> BufferBody = new("Collector.BufferBody");
}
