using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Remote.Auth;

public sealed class EntraIdSignInServiceTests
{
    private const string AccessToken = "entra-access-token";
    private const string UserCode = "ABCD1234";

    private readonly FakeEntraClient _client = new();
    private readonly RemoteAuthState _state = new();

    private EntraIdSignInService Service() => new(_client, _state);

    private RemoteAuthMethod Active => _state.MethodFor(SourceType.AzureDevops);

    [Fact]
    public async Task SignInAsync_SignedIn_SetsEntraIdState()
    {
        _client.Interactive = new EntraSignInOutcome.SignedIn(AccessToken);

        var outcome = await Service().SignInAsync(TestSupport.Ct);

        Assert.IsType<EntraSignInOutcome.SignedIn>(outcome);
        Assert.Equal(RemoteAuthMethod.EntraId, Active);
    }

    [Fact]
    public async Task SignInWithCodeAsync_SignedIn_PassesTheCodeToTheCallbackAndSetsState()
    {
        EntraDeviceCode? shown = null;
        _client.Code = new EntraDeviceCode(UserCode, new Uri("https://microsoft.com/devicelogin"), DateTimeOffset.UnixEpoch);
        _client.DeviceCode = new EntraSignInOutcome.SignedIn(AccessToken);

        var outcome = await Service().SignInWithCodeAsync(
            code =>
            {
                shown = code;
                return Task.CompletedTask;
            },
            TestSupport.Ct);

        Assert.IsType<EntraSignInOutcome.SignedIn>(outcome);
        Assert.Equal(UserCode, shown?.UserCode);
        Assert.Equal(RemoteAuthMethod.EntraId, Active);
    }

    [Fact]
    public async Task SignInWithCodeAsync_Expired_LeavesStatePat()
    {
        _client.DeviceCode = new EntraSignInOutcome.Expired();

        var outcome = await Service().SignInWithCodeAsync(_ => Task.CompletedTask, TestSupport.Ct);

        Assert.IsType<EntraSignInOutcome.Expired>(outcome);
        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Fact]
    public async Task SignInAsync_Cancelled_LeavesStatePat()
    {
        _client.Interactive = new EntraSignInOutcome.Cancelled();

        var outcome = await Service().SignInAsync(TestSupport.Ct);

        Assert.IsType<EntraSignInOutcome.Cancelled>(outcome);
        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Theory]
    [InlineData(EntraFailure.NoWindow)]
    [InlineData(EntraFailure.Unavailable)]
    [InlineData(EntraFailure.Other)]
    public async Task SignInAsync_Failed_LeavesStatePat(EntraFailure reason)
    {
        _client.Interactive = new EntraSignInOutcome.Failed(reason);

        var outcome = await Service().SignInAsync(TestSupport.Ct);

        Assert.Equal(reason, Assert.IsType<EntraSignInOutcome.Failed>(outcome).Reason);
        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Fact]
    public async Task SignInAsync_Denied_LeavesStatePat()
    {
        _client.Interactive = new EntraSignInOutcome.Denied();

        await Service().SignInAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Fact]
    public async Task SignOutAsync_ResetsStateAndSignsOutThePort()
    {
        _state.Set(SourceType.AzureDevops, RemoteAuthMethod.EntraId);

        await Service().SignOutAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.Pat, Active);
        Assert.Equal(1, _client.SignOutCalls);
    }

    [Fact]
    public async Task SignOutAsync_PortThrows_StillResetsState()
    {
        _state.Set(SourceType.AzureDevops, RemoteAuthMethod.EntraId);
        _client.SignOutFails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().SignOutAsync(TestSupport.Ct));

        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Fact]
    public async Task RestoreAsync_AccountPresent_SetsEntraIdWithoutPrompting()
    {
        _client.HasAccount = true;

        await Service().RestoreAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.EntraId, Active);
        Assert.Equal(0, _client.InteractiveCalls);
        Assert.Equal(0, _client.DeviceCodeCalls);
    }

    [Fact]
    public async Task RestoreAsync_NoAccount_LeavesStatePat()
    {
        await Service().RestoreAsync(TestSupport.Ct);

        Assert.Equal(RemoteAuthMethod.Pat, Active);
    }

    [Fact]
    public async Task TokenSource_SilentSignedIn_ReturnsOAuthToken()
    {
        _client.Silent = new EntraSignInOutcome.SignedIn(AccessToken);

        var token = await new EntraIdTokenSource(_client).GetAsync(TestSupport.Ct);

        Assert.Equal(new RemoteAccessToken(AccessToken, RemoteTokenKind.OAuth), token);
    }

    [Fact]
    public async Task TokenSource_SilentFails_ReturnsNull()
    {
        var token = await new EntraIdTokenSource(_client).GetAsync(TestSupport.Ct);

        Assert.Null(token);
    }

    [Fact]
    public void TokenSource_Identity_IsAzureDevOpsEntraId()
    {
        var source = new EntraIdTokenSource(_client);

        Assert.Equal(SourceType.AzureDevops, source.Provider);
        Assert.Equal(RemoteAuthMethod.EntraId, source.Method);
    }

    [Fact]
    public void SignedInOutcome_ToString_HidesTheToken()
    {
        Assert.DoesNotContain(AccessToken, new EntraSignInOutcome.SignedIn(AccessToken).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceCode_ToString_HidesTheUserCode()
    {
        var code = new EntraDeviceCode(UserCode, new Uri("https://microsoft.com/devicelogin"), DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(UserCode, code.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeEntraClient : IEntraIdAuthClient
    {
        public EntraSignInOutcome Silent { get; set; } = new EntraSignInOutcome.Failed(EntraFailure.Unavailable);

        public EntraSignInOutcome Interactive { get; set; } = new EntraSignInOutcome.Failed(EntraFailure.Other);

        public EntraSignInOutcome DeviceCode { get; set; } = new EntraSignInOutcome.Failed(EntraFailure.Other);

        public EntraDeviceCode? Code { get; set; }

        public bool HasAccount { get; set; }

        public bool SignOutFails { get; set; }

        public int InteractiveCalls { get; private set; }

        public int DeviceCodeCalls { get; private set; }

        public int SignOutCalls { get; private set; }

        public Task<EntraSignInOutcome> AcquireSilentAsync(CancellationToken cancellationToken) => Task.FromResult(Silent);

        public Task<EntraSignInOutcome> AcquireInteractiveAsync(CancellationToken cancellationToken)
        {
            InteractiveCalls++;
            return Task.FromResult(Interactive);
        }

        public async Task<EntraSignInOutcome> AcquireWithDeviceCodeAsync(
            Func<EntraDeviceCode, Task> onCode,
            CancellationToken cancellationToken)
        {
            DeviceCodeCalls++;
            if (Code is not null)
            {
                await onCode(Code);
            }

            return DeviceCode;
        }

        public Task<bool> HasAccountAsync(CancellationToken cancellationToken) => Task.FromResult(HasAccount);

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOutCalls++;
            return SignOutFails ? Task.FromException(new InvalidOperationException("port failed")) : Task.CompletedTask;
        }
    }
}
