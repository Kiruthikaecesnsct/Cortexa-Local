using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote.Auth;

public sealed class GitHubSignInServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemorySecretStore _secrets = new();
    private readonly ScriptedDeviceClient _client;
    private readonly CredentialManagerOAuthVault _vault;
    private readonly RemoteAuthState _state = new();

    public GitHubSignInServiceTests()
    {
        _client = new ScriptedDeviceClient(_time);
        _vault = new CredentialManagerOAuthVault(_secrets, NullLogger<CredentialManagerOAuthVault>.Instance);
    }

    private static DeviceCodeGrant ImmediateGrant() => OAuthData.Grant(TimeSpan.Zero);

    private GitHubSignInService Service(RemoteAuthAvailability? availability = null) =>
        new(new DeviceFlowRunner(_client, _time), _vault, _state, availability ?? new RemoteAuthAvailability(true, true));

    [Fact]
    public async Task CompleteAsync_Success_SavesTheTokensAndMakesTheMethodActive()
    {
        _client.Enqueue(new DevicePollResult.Success(OAuthData.Tokens("access")));
        var service = Service();

        var outcome = await service.CompleteAsync(RemoteAuthMethod.GitHubDevice, ImmediateGrant(), TestSupport.Ct);

        Assert.IsType<DeviceFlowOutcome.SignedIn>(outcome);
        Assert.True(_secrets.Values.ContainsKey(SecretSlot.GitHubOAuthToken));
        Assert.Equal(RemoteAuthMethod.GitHubDevice, service.ActiveMethod);
        Assert.True(service.IsSignedIn);
    }

    [Fact]
    public async Task CompleteAsync_SignInWithTheOtherMethod_DeletesThePreviousSlot()
    {
        _secrets.Values[SecretSlot.GitHubOAuthToken] = "{\"access\":\"old\"}";
        _client.Enqueue(new DevicePollResult.Success(OAuthData.Tokens("app-access")));

        await Service().CompleteAsync(RemoteAuthMethod.GitHubApp, ImmediateGrant(), TestSupport.Ct);

        Assert.False(_secrets.Values.ContainsKey(SecretSlot.GitHubOAuthToken));
        Assert.True(_secrets.Values.ContainsKey(SecretSlot.GitHubAppToken));
    }

    [Fact]
    public async Task CompleteAsync_CancelledRightAfterSuccess_StoresNothingAndKeepsPat()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        _client.OnPoll = _ => cts.CancelAsync();
        _client.Enqueue(new DevicePollResult.Success(OAuthData.Tokens("access")));
        var service = Service();

        var outcome = await service.CompleteAsync(RemoteAuthMethod.GitHubDevice, ImmediateGrant(), cts.Token);

        Assert.IsType<DeviceFlowOutcome.Cancelled>(outcome);
        Assert.Empty(_secrets.Values);
        Assert.Equal(RemoteAuthMethod.Pat, service.ActiveMethod);
    }

    [Fact]
    public async Task CompleteAsync_Denied_StoresNothing()
    {
        _client.Enqueue(new DevicePollResult.Denied());

        var outcome = await Service().CompleteAsync(RemoteAuthMethod.GitHubDevice, ImmediateGrant(), TestSupport.Ct);

        Assert.IsType<DeviceFlowOutcome.Denied>(outcome);
        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task StartAsync_MethodNotEnabled_Throws()
    {
        var service = Service(new RemoteAuthAvailability(false, true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));
    }

    [Fact]
    public async Task StartAsync_MethodEnabled_ReturnsTheGrant()
    {
        var grant = await Service().StartAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);

        Assert.Equal(OAuthData.UserCode, grant.UserCode);
    }

    [Fact]
    public async Task SignOutAsync_StoreDeleteFails_StillResetsTheMethod()
    {
        var vault = new CredentialManagerOAuthVault(new FailingDeleteStore(), NullLogger<CredentialManagerOAuthVault>.Instance);
        var service = new GitHubSignInService(new DeviceFlowRunner(_client, _time), vault, _state, new RemoteAuthAvailability(true, true));
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubApp);

        await Assert.ThrowsAsync<SecretStoreException>(() => service.SignOutAsync(TestSupport.Ct));

        Assert.Equal(RemoteAuthMethod.Pat, service.ActiveMethod);
    }

    [Fact]
    public async Task SignOutAsync_DeletesBothSlotsAndResetsTheMethod()
    {
        _secrets.Values[SecretSlot.GitHubOAuthToken] = "{\"access\":\"a\"}";
        _secrets.Values[SecretSlot.GitHubAppToken] = "{\"access\":\"b\"}";
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubApp);
        var service = Service();

        await service.SignOutAsync(TestSupport.Ct);

        Assert.Empty(_secrets.Values);
        Assert.Equal(RemoteAuthMethod.Pat, service.ActiveMethod);
        Assert.False(service.IsSignedIn);
        Assert.Null(await _vault.ReadAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct));
    }

    [Fact]
    public async Task RestoreAsync_StoredAppToken_MakesTheAppMethodActive()
    {
        _secrets.Values[SecretSlot.GitHubAppToken] = "{\"access\":\"b\"}";
        var service = Service();

        await service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.GitHubApp, service.ActiveMethod);
    }

    [Fact]
    public async Task RestoreAsync_NothingStored_StaysOnPat()
    {
        var service = Service();

        await service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.Pat, service.ActiveMethod);
    }

    private sealed class FailingDeleteStore : ISecretStore
    {
        public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken) =>
            throw new SecretStoreException("store unavailable");
    }
}
