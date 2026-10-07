using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Domain.Serialization;
using Collector.Infrastructure.Auth.Wire;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Auth;

public sealed class GatewayAuthClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GatewayOptions> gateway,
    TimeProvider time,
    ILogger<GatewayAuthClient> logger) : IAuthClient
{
    private const string LoginPath = "auth/login";
    private const string RefreshPath = "auth/refresh";
    private const string CookieHeader = "Cookie";
    private const string SetCookieHeader = "Set-Cookie";

    public Task<AuthCallResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var request = CreateRequest(LoginPath);
        request.Content = JsonContent.Create(new LoginBody(email, password), options: CollectorJson.Options);
        return SendAsync(request, cancellationToken);
    }

    public Task<AuthCallResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var request = CreateRequest(RefreshPath);
        request.Headers.TryAddWithoutValidation(CookieHeader, $"{RefreshCookieParser.CookieName}={refreshToken}");
        return SendAsync(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var baseUrl = gateway.CurrentValue.BaseUrl.TrimEnd('/');
        return new HttpRequestMessage(HttpMethod.Post, new Uri($"{baseUrl}/{path}"));
    }

    private async Task<AuthCallResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                var client = httpClientFactory.CreateClient(HttpClientNames.CortexaAuth);
                using var response = await client.SendAsync(request, cancellationToken);
                return await ReadAsync(response, cancellationToken);
            }
            catch (HttpRequestException)
            {
                logger.LogWarning("Gateway request failed: unreachable.");
                return AuthCallResult.Fail(AuthFailureKind.Unreachable);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Gateway request failed: timed out.");
                return AuthCallResult.Fail(AuthFailureKind.Unreachable);
            }
        }
    }

    private async Task<AuthCallResult> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var envelope = await TryReadEnvelopeAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = RetryAfterParser.Parse(response.Headers.RetryAfter, time.GetUtcNow());
            return AuthCallResult.Fail(AuthResponseMapper.Map(response.StatusCode, envelope?.ErrorCode, retryAfter));
        }

        return BuildSuccess(response, envelope);
    }

    private static AuthCallResult BuildSuccess(HttpResponseMessage response, ApiEnvelope<TokenData>? envelope)
    {
        var data = envelope?.Data;
        var refreshToken = response.Headers.TryGetValues(SetCookieHeader, out var cookies)
            ? RefreshCookieParser.TryGetRefreshToken(cookies)
            : null;
        if (envelope is not { Success: true } || data is not { AccessToken: { Length: > 0 } access, ExpiresAt: { } expires }
            || refreshToken is null)
        {
            return AuthCallResult.Fail(AuthFailureKind.UnexpectedResponse);
        }

        return AuthCallResult.Success(new AuthTokens(access, expires, refreshToken));
    }

    private static async Task<ApiEnvelope<TokenData>?> TryReadEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiEnvelope<TokenData>>(
                CollectorJson.Options,
                cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
