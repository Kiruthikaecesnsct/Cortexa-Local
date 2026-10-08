using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRemoteFileCache
{
    string? ResolvePath(SourceType provider, string repoKey, string branch, string path);

    bool Exists(string localPath);

    Task<RemoteCacheWriteResult> WriteAsync(string localPath, Stream content, CancellationToken cancellationToken);
}
