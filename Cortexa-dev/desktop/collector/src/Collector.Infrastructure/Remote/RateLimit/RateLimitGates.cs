using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.RateLimit;

public sealed class RateLimitGates : IRateLimitMonitor, IDisposable
{
    private readonly Dictionary<SourceType, RateLimitGate> _gates;

    public RateLimitGates(IOptions<RemoteSourceOptions> options, TimeProvider time)
    {
        var value = options.Value;
        _gates = new Dictionary<SourceType, RateLimitGate>
        {
            [SourceType.Github] = new(SourceType.Github, value.GitHub.MinRemaining, value.RateLimit, time),
            [SourceType.AzureDevops] = new(SourceType.AzureDevops, value.AzureDevOps.MinRemaining, value.RateLimit, time),
        };
        foreach (var gate in _gates.Values)
        {
            gate.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
        }
    }

    public event EventHandler<RateLimitStatus>? StatusChanged;

    public RateLimitGate For(SourceType provider) => _gates[provider];

    public RateLimitStatus GetStatus(SourceType provider) =>
        _gates.TryGetValue(provider, out var gate) ? gate.Status : RateLimitStatus.Running(provider);

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }
    }
}
