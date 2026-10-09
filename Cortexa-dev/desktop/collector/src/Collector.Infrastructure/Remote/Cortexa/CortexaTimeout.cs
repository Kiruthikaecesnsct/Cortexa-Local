using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote.Cortexa;

internal static class CortexaTimeout
{
    public static async Task<T> RunAsync<T>(
        int seconds,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            return await action(linked.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo, null, exception);
        }
    }
}
