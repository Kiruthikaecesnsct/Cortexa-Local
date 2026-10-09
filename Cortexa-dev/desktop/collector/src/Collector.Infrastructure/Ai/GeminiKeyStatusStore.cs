using System.Collections.Concurrent;
using Collector.Application.Ai;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiKeyStatusStore : IGeminiKeyStatusStore
{
    private readonly ConcurrentDictionary<string, GeminiKeyStatus> _statuses = new();

    public event EventHandler? Changed;

    public GeminiKeyStatus GetStatus(string keyId) =>
        _statuses.TryGetValue(keyId, out var status) ? status : GeminiKeyStatus.Untested;

    public void SetStatus(string keyId, GeminiKeyStatus status)
    {
        _statuses[keyId] = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<string, GeminiKeyStatus> Snapshot() =>
        new Dictionary<string, GeminiKeyStatus>(_statuses);
}
