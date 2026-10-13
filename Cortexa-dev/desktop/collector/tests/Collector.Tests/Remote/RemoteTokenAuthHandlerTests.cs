using System.Net;
using System.Text;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;

namespace Collector.Tests.Remote;

public sealed class RemoteTokenAuthHandlerTests
{
    private const string Token = "ghp_secret";
    private const string OAuthToken = "ghu_oauth";
    private const string BaseUrl = "https://api.github.com/";
    private const string OriginUrl = "https://api.github.com/user";
    private const string AuthorizationHeader = "Authorization";

    private readonly InMemorySessionCredentials _credentials = new();
    private readonly RemoteAuthState _state = new();
    private readonly FixedTokenSource _oauth = new(RemoteAuthMethod.GitHubDevice);

    private (HttpClient Client, StubHttpHandler Inner) Build(PatScheme scheme, string baseUrl = BaseUrl)
    {
        var inner = StubHttpHandler.Returning(HttpStatusCode.OK);
        var resolver = new RemoteTokenResolver(
            _state,
            [new SessionPatTokenSource(_credentials, SourceType.Github), _oauth]);
        var handler = new RemoteTokenAuthHandler(
            resolver,
            scheme,
            new PatTarget(SourceType.Github, new Uri(baseUrl)))
        { InnerHandler = inner };
        return (new HttpClient(handler), inner);
    }

    [Fact]
    public async Task SendAsync_BearerScheme_SendsBearerToken()
    {
        _credentials.SetToken(SourceType.Github, Token);
        var (client, inner) = Build(PatScheme.Bearer);

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.Equal($"Bearer {Token}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }

    [Fact]
    public async Task SendAsync_BasicScheme_SendsBase64OfColonAndToken()
    {
        _credentials.SetToken(SourceType.Github, Token);
        var (client, inner) = Build(PatScheme.Basic);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{Token}"));

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.Equal($"Basic {expected}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }

    [Theory]
    [InlineData("https://evil.example/user")]
    [InlineData("https://api.github.com.evil.example/user")]
    [InlineData("http://api.github.com/user")]
    [InlineData("https://api.github.com:8443/user")]
    public async Task SendAsync_OtherOrigin_SendsNoCredentialsAndIgnoresMissingToken(string url)
    {
        var (client, inner) = Build(PatScheme.Bearer);

        await client.GetAsync(url, TestSupport.Ct);

        Assert.False(inner.Requests.Single().Headers.ContainsKey(AuthorizationHeader));
    }

    [Fact]
    public async Task SendAsync_HostCaseDiffers_StillAuthenticates()
    {
        _credentials.SetToken(SourceType.Github, Token);
        var (client, inner) = Build(PatScheme.Bearer, "https://API.GitHub.com/");

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.True(inner.Requests.Single().Headers.ContainsKey(AuthorizationHeader));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_NoTokenSaved_ThrowsMissingTokenWithoutSending(string? stored)
    {
        if (stored is not null)
        {
            _credentials.SetToken(SourceType.Github, stored);
        }

        var (client, inner) = Build(PatScheme.Bearer);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(OriginUrl, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.MissingToken, exception.Kind);
        Assert.Equal(SourceType.Github, exception.Provider);
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_TokenChangesBetweenRequests_UsesTheLatestToken()
    {
        var (client, inner) = Build(PatScheme.Bearer);
        _credentials.SetToken(SourceType.Github, "first");
        await client.GetAsync(OriginUrl, TestSupport.Ct);
        _credentials.SetToken(SourceType.Github, "second");

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.Equal(["Bearer first", "Bearer second"], inner.Requests.Select(request => request.Headers[AuthorizationHeader]));
    }

    [Fact]
    public async Task SendAsync_TokenForAnotherProvider_IsNotUsed()
    {
        _credentials.SetToken(SourceType.AzureDevops, Token);
        var (client, inner) = Build(PatScheme.Bearer);

        await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(OriginUrl, TestSupport.Ct));

        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_ExistingAuthorizationHeader_IsReplaced()
    {
        _credentials.SetToken(SourceType.Github, Token);
        var (client, inner) = Build(PatScheme.Bearer);
        using var request = new HttpRequestMessage(HttpMethod.Get, OriginUrl);
        request.Headers.TryAddWithoutValidation(AuthorizationHeader, "Bearer stale");

        await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal($"Bearer {Token}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }

    [Fact]
    public async Task SendAsync_OAuthMethodActive_SendsBearerEvenWhenTheSchemeIsBasic()
    {
        _credentials.SetToken(SourceType.Github, Token);
        _oauth.Value = OAuthToken;
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubDevice);
        var (client, inner) = Build(PatScheme.Basic);

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.Equal($"Bearer {OAuthToken}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }

    [Fact]
    public async Task SendAsync_OAuthMethodActiveWithoutAToken_ThrowsMissingToken()
    {
        _credentials.SetToken(SourceType.Github, Token);
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubDevice);
        var (client, inner) = Build(PatScheme.Bearer);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(OriginUrl, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.MissingToken, exception.Kind);
        Assert.Empty(inner.Requests);
    }

    private sealed class FixedTokenSource(RemoteAuthMethod method) : IRemoteTokenSource
    {
        public string? Value { get; set; }

        public SourceType Provider => SourceType.Github;

        public RemoteAuthMethod Method => method;

        public Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Value is null ? null : new RemoteAccessToken(Value, RemoteTokenKind.OAuth));
    }
}
