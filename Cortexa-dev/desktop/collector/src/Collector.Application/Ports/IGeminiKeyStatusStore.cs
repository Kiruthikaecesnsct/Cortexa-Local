using Collector.Application.Ai;

namespace Collector.Application.Ports;

public interface IGeminiKeyStatusStore
{
    event EventHandler? Changed;

    GeminiKeyStatus GetStatus(string keyId);

    void SetStatus(string keyId, GeminiKeyStatus status);

    IReadOnlyDictionary<string, GeminiKeyStatus> Snapshot();
}
