using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubDeviceAuthorizationClient(
    IHttpClientFactory factory,
    IOptionsMonitor<RemoteSourceOptions> options,
    TimeProvider time) : IDeviceAuthorizationClient
{
    private const string DeviceCodePath = "login/device/code";
    private const string TokenPath = "login/oauth/access_token";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private const string RefreshGrantType = "refresh_token";
    private const string UserAgent = "CortexaCollector";
    private const string DeviceFlowDisabledError = "device_flow_disabled";
    private const string BadRefreshTokenError = "bad_refresh_token";
    private const int DefaultIntervalSeconds = 5;
    private const int DefaultExpirySeconds = 900;

    private static readonly Dictionary<string, Func<GitHubTokenWire, DevicePollResult>> PollErrors = new()
    {
        ["authorization_pending"] = _ => new DevicePollResult.Pending(),
        ["slow_down"] = wire => new DevicePollResult.SlowDown(wire.Interval is > 0 ? TimeSpan.FromSeconds(wire.Interval.Value) : null),
        ["expired_token"] = _ => new DevicePollResult.Expired(),
        ["access_denied"] = _ => new DevicePollResult.Denied(),
        [DeviceFlowDisabledError] = _ => new DevicePollResult.Failed(DeviceFlowFailure.DeviceFlowDisabled),
    };

    public async Task<DeviceCodeGrant> RequestCodeAsync(RemoteAuthMethod method, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string> { ["client_id"] = ClientId(method) };
        if (method == RemoteAuthMethod.GitHubDevice)
        {
            fields["scope"] = options.CurrentValue.GitHub.OAuthScopes;
        }

        try
        {
            var wire = await PostAsync(DeviceCodePath, fields, GitHubAuthJson.Default.GitHubDeviceCodeWire, cancellationToken);
            return ToGrant(wire);
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            throw new DeviceFlowException(DeviceFlowFailure.Network, exception);
        }
    }

    public async Task<DevicePollResult> PollAsync(
        RemoteAuthMethod method,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = ClientId(method),
            ["device_code"] = deviceCode,
            ["grant_type"] = DeviceGrantType,
        };
        try
        {
            var wire = await PostAsync(TokenPath, fields, GitHubAuthJson.Default.GitHubTokenWire, cancellationToken);
            return ToPollResult(wire);
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            return new DevicePollResult.Failed(DeviceFlowFailure.Network);
        }
    }

    public async Task<RefreshResult> RefreshAsync(
        RemoteAuthMethod method,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = ClientId(method),
            ["grant_type"] = RefreshGrantType,
            ["refresh_token"] = refreshToken,
        };
        try
        {
            var wire = await PostAsync(TokenPath, fields, GitHubAuthJson.Default.GitHubTokenWire, cancellationToken);
            return ToRefreshResult(wire);
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            return new RefreshResult.Failed(DeviceFlowFailure.Network);
        }
    }

    private static bool IsNetworkFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static DeviceCodeGrant ToGrant(GitHubDeviceCodeWire wire)
    {
        if (!string.IsNullOrEmpty(wire.Error))
        {
            throw new DeviceFlowException(wire.Error == DeviceFlowDisabledError ? DeviceFlowFailure.DeviceFlowDisabled : DeviceFlowFailure.Other);
        }

        var valid = !string.IsNullOrEmpty(wire.DeviceCode)
            && !string.IsNullOrEmpty(wire.UserCode)
            && Uri.TryCreate(wire.VerificationUri, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
        if (!valid)
        {
            throw new DeviceFlowException(DeviceFlowFailure.Other);
        }

        return new DeviceCodeGrant(
            wire.DeviceCode!,
            wire.UserCode!,
            new Uri(wire.VerificationUri!),
            TimeSpan.FromSeconds(wire.ExpiresIn is > 0 ? wire.ExpiresIn.Value : DefaultExpirySeconds),
            TimeSpan.FromSeconds(wire.Interval is > 0 ? wire.Interval.Value : DefaultIntervalSeconds));
    }

    private DevicePollResult ToPollResult(GitHubTokenWire wire)
    {
        if (!string.IsNullOrEmpty(wire.Error))
        {
            return PollErrors.TryGetValue(wire.Error, out var map)
                ? map(wire)
                : new DevicePollResult.Failed(DeviceFlowFailure.Other);
        }

        return string.IsNullOrEmpty(wire.AccessToken)
            ? new DevicePollResult.Failed(DeviceFlowFailure.Other)
            : new DevicePollResult.Success(ToTokenSet(wire));
    }

    private RefreshResult ToRefreshResult(GitHubTokenWire wire)
    {
        if (wire.Error == BadRefreshTokenError)
        {
            return new RefreshResult.Rejected();
        }

        return string.IsNullOrEmpty(wire.Error) && !string.IsNullOrEmpty(wire.AccessToken)
            ? new RefreshResult.Refreshed(ToTokenSet(wire))
            : new RefreshResult.Failed(DeviceFlowFailure.Other);
    }

    private OAuthTokenSet ToTokenSet(GitHubTokenWire wire)
    {
        var now = time.GetUtcNow();
        return new OAuthTokenSet(
            wire.AccessToken!,
            wire.RefreshToken,
            wire.ExpiresIn is > 0 ? now.AddSeconds(wire.ExpiresIn.Value) : null,
            wire.RefreshTokenExpiresIn is > 0 ? now.AddSeconds(wire.RefreshTokenExpiresIn.Value) : null);
    }

    private string ClientId(RemoteAuthMethod method)
    {
        var github = options.CurrentValue.GitHub;
        return method == RemoteAuthMethod.GitHubApp ? github.AppClientId : github.OAuthClientId;
    }

    private async Task<T> PostAsync<T>(
        string path,
        Dictionary<string, string> fields,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RemoteUrl.Relative(path))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await factory.CreateClient(HttpClientNames.GitHubAuth).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
            ?? throw new JsonException("Empty response body.");
    }
}
