using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Secrets;

public sealed class StaleRemoteSecretPurgeTests
{
    private static readonly SecretSlot[] StaleSlots = [SecretSlot.GitHubPat, SecretSlot.AzureDevOpsPat, SecretSlot.SshPassphrase];

    [Fact]
    public async Task RunAsync_DeletesTheThreeStaleSlotsAndKeepsEverythingElse()
    {
        var secrets = new InMemorySecretStore();
        foreach (var slot in StaleSlots)
        {
            secrets.Values[slot] = "old";
        }

        secrets.Values[SecretSlot.CortexaAccessToken] = "access";
        secrets.Values[SecretSlot.AnthropicApiKey] = "key";

        await new StaleRemoteSecretPurge(secrets, NullLogger<StaleRemoteSecretPurge>.Instance).RunAsync(TestSupport.Ct);

        Assert.Equal([SecretSlot.CortexaAccessToken, SecretSlot.AnthropicApiKey], secrets.Values.Keys.Order());
    }

    [Fact]
    public async Task RunAsync_OAuthSlots_Survive()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values[SecretSlot.GitHubPat] = "old";
        secrets.Values[SecretSlot.GitHubOAuthToken] = "device";
        secrets.Values[SecretSlot.GitHubAppToken] = "app";

        await new StaleRemoteSecretPurge(secrets, NullLogger<StaleRemoteSecretPurge>.Instance).RunAsync(TestSupport.Ct);

        Assert.Equal([SecretSlot.GitHubOAuthToken, SecretSlot.GitHubAppToken], secrets.Values.Keys.Order());
    }

    [Fact]
    public async Task RunAsync_EntraCacheSlots_Survive()
    {
        SecretSlot[] entra =
        [
            SecretSlot.EntraIdCacheIndex,
            SecretSlot.EntraIdCachePart1,
            SecretSlot.EntraIdCachePart2,
            SecretSlot.EntraIdCachePart3,
            SecretSlot.EntraIdCachePart4,
            SecretSlot.EntraIdCachePart5,
            SecretSlot.EntraIdCachePart6,
            SecretSlot.EntraIdCachePart7,
            SecretSlot.EntraIdCachePart8,
        ];
        var secrets = new InMemorySecretStore();
        secrets.Values[SecretSlot.AzureDevOpsPat] = "old";
        foreach (var slot in entra)
        {
            secrets.Values[slot] = "cache";
        }

        await new StaleRemoteSecretPurge(secrets, NullLogger<StaleRemoteSecretPurge>.Instance).RunAsync(TestSupport.Ct);

        Assert.Equal(entra.Order(), secrets.Values.Keys.Order());
    }

    [Fact]
    public async Task RunAsync_NothingStored_Succeeds()
    {
        var secrets = new InMemorySecretStore();

        await new StaleRemoteSecretPurge(secrets, NullLogger<StaleRemoteSecretPurge>.Instance).RunAsync(TestSupport.Ct);

        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task RunAsync_StoreFailsOnOneSlot_StillTriesTheOthers()
    {
        var secrets = new FailingDeleteStore(SecretSlot.GitHubPat);
        foreach (var slot in StaleSlots)
        {
            secrets.Values[slot] = "old";
        }

        await new StaleRemoteSecretPurge(secrets, NullLogger<StaleRemoteSecretPurge>.Instance).RunAsync(TestSupport.Ct);

        Assert.Equal([SecretSlot.GitHubPat], secrets.Values.Keys);
    }

    private sealed class FailingDeleteStore(SecretSlot failing) : ISecretStore
    {
        public Dictionary<SecretSlot, string> Values { get; } = [];

        public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
            Task.FromResult(Values.TryGetValue(slot, out var value) ? value : null);

        public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken)
        {
            Values[slot] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
        {
            if (slot == failing)
            {
                throw new SecretStoreException("store unavailable");
            }

            Values.Remove(slot);
            return Task.CompletedTask;
        }
    }
}
