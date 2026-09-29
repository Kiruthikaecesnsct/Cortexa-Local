using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.DTOs;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string Username,
    Role Role,
    bool IsEnabled,
    Guid? OrganizationId)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.Username, user.Role, user.IsEnabled, user.OrganizationId);
}
