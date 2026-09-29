using System.ComponentModel.DataAnnotations;

namespace Cortexa.Identity.Application.DTOs;

public sealed record LoginRequest(
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    string Email,

    [Required]
    [MaxLength(128)]
    string Password
);
