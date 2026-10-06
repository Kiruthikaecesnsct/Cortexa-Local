using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Domain.Enums;

namespace Collector.Application.Settings;

public sealed class SettingsService(IUserSettingsStore store, ISecretStore secrets)
{
    public EndpointSettings GetEndpoints() => store.GetEndpoints();

    public async Task<SettingsSaveResult> SaveEndpointsAsync(
        EndpointSettings settings,
        CancellationToken cancellationToken)
    {
        var result = EndpointSettingsRules.Validate(settings);
        if (!result.IsValid)
        {
            return result;
        }

        var normalized = new EndpointSettings(settings.GatewayUrl.Trim(), settings.CollectorServerUrl.Trim());
        await store.SaveEndpointsAsync(normalized, cancellationToken);
        return SettingsSaveResult.Ok;
    }

    public async Task<bool> HasAiKeyAsync(CollectorProvider provider, CancellationToken cancellationToken)
    {
        var slot = AiKeySlots.For(provider);
        return slot is not null && await secrets.ReadAsync(slot.Value, cancellationToken) is not null;
    }

    public Task SetAiKeyAsync(CollectorProvider provider, string key, CancellationToken cancellationToken)
    {
        var trimmed = key.Trim();
        ArgumentException.ThrowIfNullOrEmpty(trimmed, nameof(key));
        return secrets.WriteAsync(RequireSlot(provider), trimmed, cancellationToken);
    }

    public Task ClearAiKeyAsync(CollectorProvider provider, CancellationToken cancellationToken) =>
        secrets.DeleteAsync(RequireSlot(provider), cancellationToken);

    private static SecretSlot RequireSlot(CollectorProvider provider) =>
        AiKeySlots.For(provider)
        ?? throw new InvalidOperationException($"Provider {provider} does not use a stored key.");
}
