using System.Text.Json.Serialization;

namespace Cortexa.Identity.Application.DTOs;

public sealed record RegisterResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("display_name")] string DisplayName
);
