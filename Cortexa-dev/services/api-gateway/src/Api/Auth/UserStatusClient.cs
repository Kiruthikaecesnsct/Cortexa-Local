using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Cortexa.ApiGateway.Api.Auth;

public sealed record UserStatusResult(bool Enabled, Guid SecurityStamp);

internal sealed record UserStatusResponse(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("securityStamp")] Guid SecurityStamp);

public interface IUserStatusClient
{
    Task<UserStatusResult?> GetStatusAsync(string userId, CancellationToken cancellationToken);
}

public sealed class UserStatusClient : IUserStatusClient
{
    private readonly HttpClient _httpClient;
    private readonly UserStatusSettings _settings;
    private readonly ILogger<UserStatusClient> _logger;

    public UserStatusClient(
        HttpClient httpClient,
        Microsoft.Extensions.Options.IOptions<UserStatusSettings> settings,
        ILogger<UserStatusClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<UserStatusResult?> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{userId}/status");
            request.Headers.TryAddWithoutValidation(_settings.InternalKeyHeaderName, _settings.InternalKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Identity user-status lookup for {UserId} returned {StatusCode}",
                    userId,
                    response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<UserStatusResponse>(cancellationToken);
            return payload is null ? null : new UserStatusResult(payload.Enabled, payload.SecurityStamp);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Identity user-status lookup for {UserId} failed", userId);
            return null;
        }
    }
}
