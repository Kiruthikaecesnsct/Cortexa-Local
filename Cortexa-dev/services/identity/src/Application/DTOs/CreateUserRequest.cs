using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.DTOs;

public sealed record CreateUserRequest(string Email, string Username, string Password, Role Role);
