using Collector.Application.Secrets;

namespace Collector.Application.Ports;

public interface IGeminiKeyStore
{
    Task<GeminiKeyList> GetKeysAsync(CancellationToken cancellationToken);

    Task<GeminiKey> AddKeyAsync(string rawKey, CancellationToken cancellationToken);

    Task RemoveKeyAsync(string id, CancellationToken cancellationToken);

    Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken);
}
