namespace Collector.Application.Remote;

public sealed class RemoteBlob(Stream content, long? length, IDisposable? owner = null) : IDisposable
{
    public Stream Content { get; } = content;

    public long? Length { get; } = length;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }
}
