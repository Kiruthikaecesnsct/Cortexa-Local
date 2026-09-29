using System.ComponentModel.DataAnnotations;

namespace Cortexa.Identity.Application.DTOs;

public sealed record EntraLoginRequest(
    [Required]
    string IdToken
);
