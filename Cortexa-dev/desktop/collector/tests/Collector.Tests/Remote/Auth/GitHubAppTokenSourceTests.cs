using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote.Auth;

public sealed class GitHubAppTokenSourceTests
{
    private const int ConcurrentCallers = 8;

    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly InMemoryOAuthVault _vault = new();
    private readonly RemoteAuthState _state = new();
    private readonly ScriptedDeviceClient _client;
    private readonly GitHubAppTokenSource _source;

    public GitHubAppTokenSourceTests()
    {
        _client = new ScriptedDeviceClient(_time);
        _source = new GitHubAppTokenSource(_vault, _client, _state, new OAuthRefreshPolicy(Skew), _time);
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubApp);
    }

    private static OAuthTokenSet Rotated() =>
        OAuthData.Tokens("new-access", Start + Lifetime, "new-refresh", Start + Lifetime * 2);

    [Fact]
    public async Task GetAsync_NothingStored_ReturnsNull()
    {
        Assert.Null(await _source.GetAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task GetAsync_OutsideTheSkew_ReturnsTheStoredTokenWithoutRefreshing()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start + Skew + TimeSpan.FromMinutes(1)));

        var token = await _source.GetAsync(TestSupport.Ct);

        Assert.Equal(new RemoteAccessToken("stored", RemoteTokenKind.OAuth), token);
        Assert.Equal(0, _client.RefreshCalls);
    }

    [Fact]
    public async Task GetAsync_InsideTheSkew_RefreshesAndSavesTheRotatedPairBeforeReturning()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start + TimeSpan.FromMinutes(1)));
        _client.RefreshResult = new RefreshResult.Refreshed(Rotated());

        var token = await _source.GetAsync(TestSupport.Ct);

        Assert.Equal("new-access", token!.Value);
        Assert.Equal(Rotated(), _vault.Peek(RemoteAuthMethod.GitHubApp));
        Assert.Equal(1, _vault.SaveCalls);
    }

    [Fact]
    public async Task GetAsync_ManyCallersNeedARefresh_RefreshesOnce()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.OnRefresh = async () =>
        {
            await release.Task;
            return new RefreshResult.Refreshed(Rotated());
        };

        var callers = Enumerable.Range(0, ConcurrentCallers).Select(_ => _source.GetAsync(TestSupport.Ct)).ToArray();
        release.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.Equal(1, _client.RefreshCalls);
        Assert.All(tokens, token => Assert.Equal("new-access", token!.Value));
    }

    [Fact]
    public async Task GetAsync_RefreshRejected_DeletesTheSlotResetsTheMethodAndThrowsAuth()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start));
        _client.RefreshResult = new RefreshResult.Rejected();

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => _source.GetAsync(TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Auth, exception.Kind);
        Assert.Null(_vault.Peek(RemoteAuthMethod.GitHubApp));
        Assert.Equal(RemoteAuthMethod.Pat, _state.MethodFor(SourceType.Github));
    }

    [Fact]
    public async Task GetAsync_RefreshTokenExpired_ThrowsAuthWithoutCallingGitHub()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start, "old", Start - TimeSpan.FromDays(1)));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => _source.GetAsync(TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Auth, exception.Kind);
        Assert.Equal(0, _client.RefreshCalls);
        Assert.Null(_vault.Peek(RemoteAuthMethod.GitHubApp));
    }

    [Fact]
    public async Task GetAsync_RefreshFailsButAccessTokenStillValid_ReturnsTheStoredToken()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start + TimeSpan.FromMinutes(1)));
        _client.RefreshResult = new RefreshResult.Failed(DeviceFlowFailure.Network);

        var token = await _source.GetAsync(TestSupport.Ct);

        Assert.Equal("stored", token!.Value);
        Assert.NotNull(_vault.Peek(RemoteAuthMethod.GitHubApp));
    }

    [Fact]
    public async Task GetAsync_RefreshFailsAndAccessTokenExpired_ThrowsUpstreamAndKeepsTheSlot()
    {
        _vault.Seed(RemoteAuthMethod.GitHubApp, OAuthData.Tokens("stored", Start - TimeSpan.FromMinutes(1)));
        _client.RefreshResult = new RefreshResult.Failed(DeviceFlowFailure.Network);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => _source.GetAsync(TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
        Assert.NotNull(_vault.Peek(RemoteAuthMethod.GitHubApp));
        Assert.Equal(RemoteAuthMethod.GitHubApp, _state.MethodFor(SourceType.Github));
    }
}
