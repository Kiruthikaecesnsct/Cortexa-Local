using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SSO;
using Amazon.SSO.Model;
using Amazon.SSOOIDC;
using Amazon.SSOOIDC.Model;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public interface IBedrockSsoClientFactory
{
    IAmazonSSOOIDC CreateOidcClient(RegionEndpoint region);

    IAmazonSSO CreateSsoClient(RegionEndpoint region);
}

public sealed class BedrockSsoClientFactory : IBedrockSsoClientFactory
{
    public IAmazonSSOOIDC CreateOidcClient(RegionEndpoint region) =>
        new AmazonSSOOIDCClient(new AnonymousAWSCredentials(), region);

    public IAmazonSSO CreateSsoClient(RegionEndpoint region) =>
        new AmazonSSOClient(new AnonymousAWSCredentials(), region);
}

public sealed record BedrockSsoCredentialsDependencies(
    ISecretStore Secrets,
    TimeProvider TimeProvider,
    ILogger<BedrockSsoCredentials> Logger,
    IBedrockSsoClientFactory SsoClients);

public sealed class BedrockSsoCredentials : RefreshingAWSCredentials, IBedrockSsoCredentials
{
    private const string ClientName = "Cortexa Collector";
    private const string PublicClientType = "public";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private const int DefaultPollIntervalSeconds = 5;
    private const int SlowDownIncrementSeconds = 5;
    private const int ExpirationBufferMinutes = 5;
    private const int TokenRefreshBufferMinutes = 2;
    private static readonly TimeSpan DeviceAuthorizationTimeout = TimeSpan.FromMinutes(10);

    private readonly IOptions<BedrockProviderOptions> options;
    private readonly ISecretStore secrets;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<BedrockSsoCredentials> logger;
    private readonly IBedrockSsoClientFactory ssoClients;

    public BedrockSsoCredentials(IOptions<BedrockProviderOptions> options, BedrockSsoCredentialsDependencies dependencies)
    {
        this.options = options;
        secrets = dependencies.Secrets;
        timeProvider = dependencies.TimeProvider;
        logger = dependencies.Logger;
        ssoClients = dependencies.SsoClients;
        ExpirationBuffer = TimeSpan.FromMinutes(ExpirationBufferMinutes);
    }

    public async Task<BedrockSsoStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await ReadPersistedTokenAsync(cancellationToken).ConfigureAwait(false);
        return token is null ? BedrockSsoStatus.NotConnected : new BedrockSsoStatus(true, token.ExpiresAtUtc);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GetCredentialsAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await secrets.DeleteAsync(SecretSlot.BedrockSsoToken, cancellationToken).ConfigureAwait(false);
        ClearCredentials();
    }

    protected override async Task<CredentialsRefreshState> GenerateNewCredentialsAsync()
    {
        using var cts = new CancellationTokenSource(DeviceAuthorizationTimeout);
        var token = await GetOrCreateSsoTokenAsync(cts.Token).ConfigureAwait(false);
        var roleCredentials = await FetchRoleCredentialsAsync(token, cts.Token).ConfigureAwait(false);
        return BuildRefreshState(roleCredentials);
    }

    private async Task<string> GetOrCreateSsoTokenAsync(CancellationToken cancellationToken)
    {
        var cached = await ReadPersistedTokenAsync(cancellationToken).ConfigureAwait(false);
        if (cached is { } token && token.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(TokenRefreshBufferMinutes))
        {
            return token.AccessToken;
        }

        var fresh = await AuthorizeDeviceAsync(cancellationToken).ConfigureAwait(false);
        await PersistTokenAsync(fresh, cancellationToken).ConfigureAwait(false);
        return fresh.AccessToken;
    }

    private async Task<BedrockSsoToken?> ReadPersistedTokenAsync(CancellationToken cancellationToken)
    {
        var raw = await secrets.ReadAsync(SecretSlot.BedrockSsoToken, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BedrockSsoToken>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task PersistTokenAsync(BedrockSsoToken token, CancellationToken cancellationToken) =>
        secrets.WriteAsync(SecretSlot.BedrockSsoToken, JsonSerializer.Serialize(token), cancellationToken);

    private async Task<BedrockSsoToken> AuthorizeDeviceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var region = RegionEndpoint.GetBySystemName(settings.SsoRegion);
        using var oidcClient = ssoClients.CreateOidcClient(region);
        var client = await RegisterClientAsync(oidcClient, cancellationToken).ConfigureAwait(false);
        var authorization = await StartDeviceAuthorizationAsync(oidcClient, client, settings.SsoStartUrl, cancellationToken)
            .ConfigureAwait(false);
        LogSignInPrompt(authorization.VerificationUriComplete);
        return await PollForTokenAsync(oidcClient, client, authorization, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RegisteredClient> RegisterClientAsync(
        IAmazonSSOOIDC oidcClient,
        CancellationToken cancellationToken)
    {
        var response = await oidcClient.RegisterClientAsync(
            new RegisterClientRequest { ClientName = ClientName, ClientType = PublicClientType },
            cancellationToken).ConfigureAwait(false);
        return new RegisteredClient(response.ClientId, response.ClientSecret);
    }

    private static Task<StartDeviceAuthorizationResponse> StartDeviceAuthorizationAsync(
        IAmazonSSOOIDC oidcClient,
        RegisteredClient client,
        string startUrl,
        CancellationToken cancellationToken) =>
        oidcClient.StartDeviceAuthorizationAsync(
            new StartDeviceAuthorizationRequest
            {
                ClientId = client.ClientId,
                ClientSecret = client.ClientSecret,
                StartUrl = startUrl,
            },
            cancellationToken);

    private async Task<BedrockSsoToken> PollForTokenAsync(
        IAmazonSSOOIDC oidcClient,
        RegisteredClient client,
        StartDeviceAuthorizationResponse authorization,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(authorization.Interval ?? DefaultPollIntervalSeconds, DefaultPollIntervalSeconds));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RequestTokenAsync(oidcClient, client, authorization.DeviceCode, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthorizationPendingException)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (SlowDownException)
            {
                interval += TimeSpan.FromSeconds(SlowDownIncrementSeconds);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<BedrockSsoToken> RequestTokenAsync(
        IAmazonSSOOIDC oidcClient,
        RegisteredClient client,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var response = await oidcClient.CreateTokenAsync(
            new CreateTokenRequest
            {
                ClientId = client.ClientId,
                ClientSecret = client.ClientSecret,
                DeviceCode = deviceCode,
                GrantType = DeviceGrantType,
            },
            cancellationToken).ConfigureAwait(false);
        var expiresAt = timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn ?? 0);
        return new BedrockSsoToken(response.AccessToken, expiresAt);
    }

    private async Task<RoleCredentials> FetchRoleCredentialsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var region = RegionEndpoint.GetBySystemName(settings.SsoRegion);
        using var ssoClient = ssoClients.CreateSsoClient(region);
        var response = await ssoClient.GetRoleCredentialsAsync(
            new GetRoleCredentialsRequest
            {
                AccessToken = accessToken,
                AccountId = settings.AccountId,
                RoleName = settings.SsoRoleName,
            },
            cancellationToken).ConfigureAwait(false);
        return response.RoleCredentials;
    }

    private static CredentialsRefreshState BuildRefreshState(RoleCredentials credentials)
    {
        var immutable = new ImmutableCredentials(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken);
        var expiresAtMs = credentials.Expiration
            ?? throw new InvalidOperationException("AWS did not return an expiration for the role credentials.");
        return new CredentialsRefreshState(immutable, DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs).UtcDateTime);
    }

    private void LogSignInPrompt(string verificationUriComplete) =>
        logger.LogInformation(
            "Sign in to AWS IAM Identity Center to continue using Bedrock: {VerificationUri}",
            verificationUriComplete);

    private sealed record RegisteredClient(string ClientId, string? ClientSecret);

    private sealed record BedrockSsoToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
