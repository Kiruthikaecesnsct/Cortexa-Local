using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Collector.Server.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Identity;

public sealed class HttpUserStatusReader(
    HttpClient httpClient,
    IOptions<IdentityOptions> options,
    ILogger<HttpUserStatusReader> logger) : IUserStatusReader
{
    public async Task<UserStatus?> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildRequest(userId);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Identity status lookup for user {UserId} returned {StatusCode}.",
                    userId,
                    (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusPayload>(cancellationToken);
            return payload is null ? null : new UserStatus(payload.Enabled, payload.SecurityStamp);
        }
        catch (Exception exception) when (IsLookupFailure(exception, cancellationToken))
        {
            logger.LogWarning(
                "Identity status lookup for user {UserId} failed: {FailureType}.",
                userId,
                exception.GetType().Name);
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(string userId)
    {
        var path = $"/internal/users/{Uri.EscapeDataString(userId)}/status";
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var settings = options.Value;
        request.Headers.TryAddWithoutValidation(settings.InternalKeyHeaderName, settings.InternalKey);
        return request;
    }

    private static bool IsLookupFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record StatusPayload(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("securityStamp")] Guid SecurityStamp);
}
