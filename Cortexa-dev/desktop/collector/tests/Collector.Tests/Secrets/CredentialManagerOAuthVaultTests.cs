using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Secrets;

public sealed class CredentialManagerOAuthVaultTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 10, 13, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RefreshExpiry = new(2027, 4, 13, 17, 0, 0, TimeSpan.Zero);

    private readonly InMemorySecretStore _secrets = new();

    private CredentialManagerOAuthVault Vault(ISecretStore? store = null) =>
        new(store ?? _secrets, NullLogger<CredentialManagerOAuthVault>.Instance);

    [Theory]
    [InlineData(RemoteAuthMethod.GitHubDevice, SecretSlot.GitHubOAuthToken)]
    [InlineData(RemoteAuthMethod.GitHubApp, SecretSlot.GitHubAppToken)]
    public async Task SaveAsync_ThenReadAsync_RoundTripsThroughTheMatchingSlot(RemoteAuthMethod method, SecretSlot slot)
    {
        var vault = Vault();
        var tokens = OAuthData.Tokens("access", Expiry, "refresh", RefreshExpiry);

        await vault.SaveAsync(method, tokens, TestSupport.Ct);
        var fresh = await Vault().ReadAsync(method, TestSupport.Ct);

        Assert.True(_secrets.Values.ContainsKey(slot));
        Assert.Equal(tokens, fresh);
    }

    [Fact]
    public async Task ReadAsync_NothingStored_ReturnsNull()
    {
        Assert.Null(await Vault().ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct));
    }

    [Fact]
    public async Task ReadAsync_SecondRead_ComesFromTheCache()
    {
        var vault = Vault();
        await vault.SaveAsync(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("access"), TestSupport.Ct);
        _secrets.Values.Clear();

        var cached = await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);

        Assert.Equal("access", cached!.AccessToken);
    }

    [Fact]
    public async Task DropCache_NextReadGoesBackToTheStore()
    {
        var vault = Vault();
        await vault.SaveAsync(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("access"), TestSupport.Ct);
        _secrets.Values.Clear();

        vault.DropCache();

        Assert.Null(await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct));
    }

    [Fact]
    public async Task ReadAsync_CorruptBlob_DeletesTheSlotAndReturnsNull()
    {
        _secrets.Values[SecretSlot.GitHubAppToken] = "not json";

        var tokens = await Vault().ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);

        Assert.Null(tokens);
        Assert.False(_secrets.Values.ContainsKey(SecretSlot.GitHubAppToken));
    }

    [Fact]
    public async Task ReadAsync_BlobWithoutAnAccessToken_IsTreatedAsMissing()
    {
        _secrets.Values[SecretSlot.GitHubAppToken] = "{\"refresh\":\"r\"}";

        Assert.Null(await Vault().ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct));
    }

    [Fact]
    public async Task ReadAsync_StoreFails_ReturnsNullAndDoesNotCacheTheMiss()
    {
        var store = new FlakyReadStore();
        var vault = Vault(store);
        store.Values[SecretSlot.GitHubAppToken] = "{\"access\":\"a\"}";
        store.Fail = true;

        var failed = await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);
        store.Fail = false;
        var recovered = await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);

        Assert.Null(failed);
        Assert.Equal("a", recovered!.AccessToken);
    }

    [Fact]
    public async Task DeleteProviderAsync_RemovesBothSlotsAndTheCache()
    {
        var vault = Vault();
        await vault.SaveAsync(RemoteAuthMethod.GitHubDevice, OAuthData.Tokens("a"), TestSupport.Ct);
        await vault.SaveAsync(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("b"), TestSupport.Ct);

        await vault.DeleteProviderAsync(SourceType.Github, TestSupport.Ct);

        Assert.Empty(_secrets.Values);
        Assert.Null(await vault.ReadAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));
        Assert.Null(await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct));
    }

    [Fact]
    public async Task SaveAsync_PatMethod_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Vault().SaveAsync(RemoteAuthMethod.Pat, OAuthData.Tokens("a"), TestSupport.Ct));
    }

    [Fact]
    public async Task SaveAsync_TypicalGitHubAppTokens_FitTheCredentialManagerLimit()
    {
        var vault = Vault();
        var tokens = OAuthData.Tokens(new string('a', 120), Expiry, new string('r', 120), RefreshExpiry);

        await vault.SaveAsync(RemoteAuthMethod.GitHubApp, tokens, TestSupport.Ct);

        Assert.True(_secrets.Values[SecretSlot.GitHubAppToken].Length < CredentialManagerStore.MaxBlobBytes);
    }

    [Fact]
    public async Task DeleteProviderAsync_OneSlotFails_StillDeletesTheOtherSlotThenThrows()
    {
        var store = new FlakyReadStore { FailDeleteOf = SecretSlot.GitHubOAuthToken };
        store.Values[SecretSlot.GitHubOAuthToken] = "{\"access\":\"a\"}";
        store.Values[SecretSlot.GitHubAppToken] = "{\"access\":\"b\"}";

        await Assert.ThrowsAsync<SecretStoreException>(
            () => Vault(store).DeleteProviderAsync(SourceType.Github, TestSupport.Ct));

        Assert.False(store.Values.ContainsKey(SecretSlot.GitHubAppToken));
    }

    [Fact]
    public async Task SaveAsync_WriteFails_ThrowsButTheNewTokensStayReadableThisSession()
    {
        var vault = Vault(new FlakyReadStore { FailWrites = true });

        await Assert.ThrowsAsync<SecretStoreException>(
            () => vault.SaveAsync(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("rotated"), TestSupport.Ct));

        Assert.Equal("rotated", (await vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct))!.AccessToken);
    }

    private sealed class FlakyReadStore : ISecretStore
    {
        public Dictionary<SecretSlot, string> Values { get; } = [];

        public bool Fail { get; set; }

        public bool FailWrites { get; set; }

        public SecretSlot? FailDeleteOf { get; set; }

        public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
            Fail
                ? throw new SecretStoreException("store unavailable")
                : Task.FromResult(Values.TryGetValue(slot, out var value) ? value : null);

        public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                throw new SecretStoreException("store unavailable");
            }

            Values[slot] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
        {
            if (slot == FailDeleteOf)
            {
                throw new SecretStoreException("store unavailable");
            }

            Values.Remove(slot);
            return Task.CompletedTask;
        }
    }
}
