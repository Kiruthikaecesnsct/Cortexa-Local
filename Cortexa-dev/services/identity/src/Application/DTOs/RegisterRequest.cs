using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cortexa.Identity.Application.DTOs;

public sealed record RegisterRequest(
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    string Email,

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    string Password,

    [Required]
    [MinLength(2)]
    [MaxLength(100)]
    [property: JsonPropertyName("display_name")]
    string DisplayName
);
