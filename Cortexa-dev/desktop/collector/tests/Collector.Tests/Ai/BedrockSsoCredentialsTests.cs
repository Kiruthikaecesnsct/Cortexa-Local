using Amazon;
using Amazon.SSO;
using Amazon.SSOOIDC;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Ai;

public sealed class BedrockSsoCredentialsTests
{
    private const string SsoRegion = "us-east-1";
    private const string StartUrl = "https://example.awsapps.com/start";
    private const string AccountId = "123456789012";
    private const string RoleName = "CortexaBedrockRole";

    private readonly InMemorySecretStore _secrets = new();
    private readonly NeverCalledSsoClientFactory _clientFactory = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    private BedrockSsoCredentials Credentials() => new(
        Options.Create(new BedrockProviderOptions
        {
            SsoRegion = SsoRegion,
            SsoStartUrl = StartUrl,
            AccountId = AccountId,
            SsoRoleName = RoleName,
        }),
        new BedrockSsoCredentialsDependencies(
            _secrets,
            _time,
            NullLogger<BedrockSsoCredentials>.Instance,
            _clientFactory));

    private static string Serialize(string accessToken, DateTimeOffset expiresAtUtc) =>
        System.Text.Json.JsonSerializer.Serialize(new { AccessToken = accessToken, ExpiresAtUtc = expiresAtUtc });

    [Fact]
    public async Task GetStatusAsync_NoPersistedToken_ReturnsNotConnected()
    {
        var status = await Credentials().GetStatusAsync(TestSupport.Ct);

        Assert.False(status.IsConnected);
        Assert.Null(status.ExpiresAtUtc);
    }

    [Fact]
    public async Task GetStatusAsync_PersistedToken_ReturnsConnectedWithExpiry()
    {
        var expiresAt = _time.GetUtcNow().AddHours(8);
        _secrets.Values[SecretSlot.BedrockSsoToken] = Serialize("token-value", expiresAt);

        var status = await Credentials().GetStatusAsync(TestSupport.Ct);

        Assert.True(status.IsConnected);
        Assert.Equal(expiresAt, status.ExpiresAtUtc);
    }

    [Fact]
    public async Task GetStatusAsync_PersistedTokenAlreadyExpired_StillReportsConnected()
    {
        var expiresAt = _time.GetUtcNow().AddHours(-2);
        _secrets.Values[SecretSlot.BedrockSsoToken] = Serialize("stale-token", expiresAt);

        var status = await Credentials().GetStatusAsync(TestSupport.Ct);

        Assert.True(status.IsConnected);
        Assert.Equal(expiresAt, status.ExpiresAtUtc);
        Assert.Equal(0, _clientFactory.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_CorruptPersistedToken_ReturnsNotConnected()
    {
        _secrets.Values[SecretSlot.BedrockSsoToken] = "{not-json";

        var status = await Credentials().GetStatusAsync(TestSupport.Ct);

        Assert.False(status.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesPersistedToken()
    {
        _secrets.Values[SecretSlot.BedrockSsoToken] = Serialize("token-value", _time.GetUtcNow().AddHours(1));
        var credentials = Credentials();

        await credentials.DisconnectAsync(TestSupport.Ct);

        Assert.False(_secrets.Values.ContainsKey(SecretSlot.BedrockSsoToken));
    }

    [Fact]
    public async Task DisconnectAsync_ThenGetStatusAsync_ReportsNotConnected()
    {
        _secrets.Values[SecretSlot.BedrockSsoToken] = Serialize("token-value", _time.GetUtcNow().AddHours(1));
        var credentials = Credentials();

        await credentials.DisconnectAsync(TestSupport.Ct);
        var status = await credentials.GetStatusAsync(TestSupport.Ct);

        Assert.False(status.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_WithoutPersistedToken_DoesNotThrow()
    {
        var credentials = Credentials();

        await credentials.DisconnectAsync(TestSupport.Ct);

        Assert.False(_secrets.Values.ContainsKey(SecretSlot.BedrockSsoToken));
    }

    [Fact]
    public void ClientFactory_CreateOidcClient_ReturnsSsoOidcClient()
    {
        using var client = new BedrockSsoClientFactory().CreateOidcClient(RegionEndpoint.USEast1);

        Assert.IsAssignableFrom<IAmazonSSOOIDC>(client);
    }

    [Fact]
    public void ClientFactory_CreateSsoClient_ReturnsSsoClient()
    {
        using var client = new BedrockSsoClientFactory().CreateSsoClient(RegionEndpoint.USEast1);

        Assert.IsAssignableFrom<IAmazonSSO>(client);
    }

    private sealed class NeverCalledSsoClientFactory : IBedrockSsoClientFactory
    {
        public int CallCount { get; private set; }

        public IAmazonSSOOIDC CreateOidcClient(RegionEndpoint region)
        {
            CallCount++;
            throw new InvalidOperationException("The AWS SSO OIDC client should not be constructed for this test.");
        }

        public IAmazonSSO CreateSsoClient(RegionEndpoint region)
        {
            CallCount++;
            throw new InvalidOperationException("The AWS SSO client should not be constructed for this test.");
        }
    }
}
