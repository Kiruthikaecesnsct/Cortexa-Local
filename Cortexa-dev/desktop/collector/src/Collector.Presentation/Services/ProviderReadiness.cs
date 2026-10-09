using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public enum ProviderReadinessState
{
    Checking,
    Ready,
    Missing,
    Unknown,
}

public interface IProviderReadiness
{
    event EventHandler? Changed;

    Task<ProviderReadinessState> CheckAsync(CollectorProvider provider, CancellationToken cancellationToken);

    Task<bool> IsReadyAsync(CollectorProvider provider, CancellationToken cancellationToken);

    void NotifyChanged();
}

public sealed class ProviderReadiness(
    SettingsService settings,
    IBedrockSsoCredentials bedrock,
    ILogger<ProviderReadiness> logger) : IProviderReadiness
{
    public event EventHandler? Changed;

    public async Task<bool> IsReadyAsync(CollectorProvider provider, CancellationToken cancellationToken) =>
        await CheckAsync(provider, cancellationToken) == ProviderReadinessState.Ready;

    public async Task<ProviderReadinessState> CheckAsync(CollectorProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            var ready = await ReadAsync(provider, cancellationToken);
            return ready ? ProviderReadinessState.Ready : ProviderReadinessState.Missing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not check whether {Provider} is ready.", provider);
            return ProviderReadinessState.Unknown;
        }
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private async Task<bool> ReadAsync(CollectorProvider provider, CancellationToken cancellationToken)
    {
        if (provider == CollectorProvider.Bedrock)
        {
            var status = await bedrock.GetStatusAsync(cancellationToken);
            return status.IsConnected;
        }

        return await settings.HasAiKeyAsync(provider, cancellationToken);
    }
}
