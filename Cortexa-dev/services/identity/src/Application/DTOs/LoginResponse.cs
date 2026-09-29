using System.Text.Json.Serialization;

namespace Cortexa.Identity.Application.DTOs;

public sealed record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);
