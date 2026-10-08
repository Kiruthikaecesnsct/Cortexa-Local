using System.Net;
using System.Text;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote;
using Collector.Tests.Support;

namespace Collector.Tests.Remote;

public sealed class PatAuthHandlerTests
{
    private const string Token = "ghp_secret";
    private const string BaseUrl = "https://api.github.com/";
    private const string OriginUrl = "https://api.github.com/user";
    private const string AuthorizationHeader = "Authorization";

    private readonly InMemorySecretStore _secrets = new();

    private (HttpClient Client, StubHttpHandler Inner) Build(PatScheme scheme, string baseUrl = BaseUrl)
    {
        var inner = StubHttpHandler.Returning(HttpStatusCode.OK);
        var handler = new PatAuthHandler(
            _secrets,
            SecretSlot.GitHubPat,
            scheme,
            new PatTarget(SourceType.Github, new Uri(baseUrl)))
        { InnerHandler = inner };
        return (new HttpClient(handler), inner);
    }

    [Fact]
    public async Task SendAsync_BearerScheme_SendsBearerToken()
    {
        _secrets.Values[SecretSlot.GitHubPat] = Token;
        var (client, inner) = Build(PatScheme.Bearer);

        await client.GetAsync(OriginUrl, TestSupport.Ct);

        Assert.Equal($"Bearer {Token}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }

    [Fact]
    public async Task SendAsync_BasicScheme_SendsBase64OfColonAndToken()
    {
        _secrets.Values[SecretSlot.GitHubPat] = Token;
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
        _secrets.Values[SecretSlot.GitHubPat] = Token;
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
            _secrets.Values[SecretSlot.GitHubPat] = stored;
        }

        var (client, inner) = Build(PatScheme.Bearer);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(OriginUrl, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.MissingToken, exception.Kind);
        Assert.Equal(SourceType.Github, exception.Provider);
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_ExistingAuthorizationHeader_IsReplaced()
    {
        _secrets.Values[SecretSlot.GitHubPat] = Token;
        var (client, inner) = Build(PatScheme.Bearer);
        using var request = new HttpRequestMessage(HttpMethod.Get, OriginUrl);
        request.Headers.TryAddWithoutValidation(AuthorizationHeader, "Bearer stale");

        await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal($"Bearer {Token}", inner.Requests.Single().Headers[AuthorizationHeader]);
    }
}
