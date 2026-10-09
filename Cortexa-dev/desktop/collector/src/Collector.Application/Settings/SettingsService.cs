using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Domain.Enums;

namespace Collector.Application.Settings;

public sealed class SettingsService(IUserSettingsStore store, ISecretStore secrets)
{
    public EndpointSettings GetEndpoints() => store.GetEndpoints();

    public RemoteSourceSettings GetRemoteSources() => store.GetRemoteSources();

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

    public async Task<SettingsSaveResult> SaveRemoteSourcesAsync(
        RemoteSourceSettings settings,
        CancellationToken cancellationToken)
    {
        var result = RemoteSourceRules.Validate(settings);
        if (!result.IsValid)
        {
            return result;
        }

        var normalized = settings with { AzureDevOpsOrganization = settings.AzureDevOpsOrganization.Trim() };
        await store.SaveRemoteSourcesAsync(normalized, cancellationToken);
        return SettingsSaveResult.Ok;
    }

    public IReadOnlyList<SshConnectionProfile> GetSshProfiles() => store.GetRemoteSources().SshProfiles;

    public Task SaveSshProfilesAsync(IReadOnlyList<SshConnectionProfile> profiles, CancellationToken cancellationToken) =>
        store.SaveRemoteSourcesAsync(store.GetRemoteSources() with { SshProfiles = profiles }, cancellationToken);

    public string? GetPinnedFingerprint(string host) =>
        GetSshProfiles().FirstOrDefault(profile => IsSameHost(profile.Host, host))?.PinnedFingerprint;

    public Task PinFingerprintAsync(string host, string fingerprint, CancellationToken cancellationToken)
    {
        var updated = GetSshProfiles()
            .Select(profile => IsSameHost(profile.Host, host) ? profile with { PinnedFingerprint = fingerprint } : profile)
            .ToArray();
        return SaveSshProfilesAsync(updated, cancellationToken);
    }

    public Task<bool> HasSshPassphraseAsync(CancellationToken cancellationToken) =>
        HasSecretAsync(SecretSlot.SshPassphrase, cancellationToken);

    public Task SetSshPassphraseAsync(string passphrase, CancellationToken cancellationToken) =>
        SetSecretAsync(SecretSlot.SshPassphrase, passphrase, cancellationToken);

    public Task ClearSshPassphraseAsync(CancellationToken cancellationToken) =>
        ClearSecretAsync(SecretSlot.SshPassphrase, cancellationToken);

    private static bool IsSameHost(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HasSecretAsync(SecretSlot slot, CancellationToken cancellationToken) =>
        await secrets.ReadAsync(slot, cancellationToken) is not null;

    public Task SetSecretAsync(SecretSlot slot, string value, CancellationToken cancellationToken) =>
        secrets.WriteAsync(slot, SecretInputRules.Normalize(value), cancellationToken);

    public Task ClearSecretAsync(SecretSlot slot, CancellationToken cancellationToken) =>
        secrets.DeleteAsync(slot, cancellationToken);

    public async Task<bool> HasAiKeyAsync(CollectorProvider provider, CancellationToken cancellationToken)
    {
        var slot = AiKeySlots.For(provider);
        return slot is not null && await HasSecretAsync(slot.Value, cancellationToken);
    }

    public Task SetAiKeyAsync(CollectorProvider provider, string key, CancellationToken cancellationToken) =>
        SetSecretAsync(RequireSlot(provider), key, cancellationToken);

    public Task ClearAiKeyAsync(CollectorProvider provider, CancellationToken cancellationToken) =>
        ClearSecretAsync(RequireSlot(provider), cancellationToken);

    public async Task<bool> HasRemoteTokenAsync(SourceType source, CancellationToken cancellationToken)
    {
        if (source == SourceType.Ssh)
        {
            return false;
        }

        var slot = RemoteSourceSlots.For(source);
        return slot is not null && await HasSecretAsync(slot.Value, cancellationToken);
    }

    private static SecretSlot RequireSlot(CollectorProvider provider) =>
        AiKeySlots.For(provider)
        ?? throw new InvalidOperationException($"Provider {provider} does not use a stored key.");
}
