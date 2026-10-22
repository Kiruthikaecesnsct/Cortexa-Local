using System.Collections.Concurrent;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiModelExhaustionTracker : IGeminiModelExhaustion
{
    private readonly ConcurrentDictionary<string, byte> _exhausted = new(StringComparer.Ordinal);

    public bool IsExhausted(string model) => _exhausted.ContainsKey(model);

    public void MarkExhausted(string model) => _exhausted[model] = 0;

    public void Reset() => _exhausted.Clear();
}
