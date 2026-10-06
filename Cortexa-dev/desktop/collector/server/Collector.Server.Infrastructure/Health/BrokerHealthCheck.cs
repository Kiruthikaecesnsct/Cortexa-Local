using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Collector.Server.Infrastructure.Health;

public sealed class BrokerHealthCheck(IBrokerProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await probe.IsAvailableAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Message broker connection is not open.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Message broker is unreachable.", exception);
        }
    }
}
