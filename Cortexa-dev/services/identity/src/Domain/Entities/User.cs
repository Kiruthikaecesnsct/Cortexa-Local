using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Domain.Entities;

public sealed class User
{
    private const Role MaxSsoAssignableRole = Role.Admin;

    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public Role Role { get; private set; }
    public string? EntraObjectId { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid SecurityStamp { get; private set; }

    public void EnsureMutable()
    {
        if (IsSystem)
            throw new ImmutableUserException();
    }

    public void AssignOrganization(Guid organizationId)
    {
        EnsureMutable();
        OrganizationId = organizationId;
    }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        EnsureMutable();
        IsEnabled = false;
        RotateSecurityStamp();
    }

    public void SetRole(Role role)
    {
        EnsureMutable();
        if (role == Role.SuperAdmin)
            throw new BadRequestException("SuperAdmin role cannot be assigned");
        Role = role;
        RotateSecurityStamp();
    }

    public void UpdateProfile(string email, string username)
    {
        EnsureMutable();
        Email = email;
        Username = username;
    }

    public void ChangePassword(string newHash)
    {
        EnsureMutable();
        PasswordHash = newHash;
        RotateSecurityStamp();
    }

    public void InvalidateSessions()
    {
        RotateSecurityStamp();
    }

    private void RotateSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid();
    }

    public static User CreateFromEntra(
        string email,
        string username,
        string entraObjectId,
        Role role,
        Guid organizationId,
        bool isSystem = false)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Username = username,
            EntraObjectId = entraObjectId,
            Role = CapToMaxSsoAssignableRole(role),
            OrganizationId = organizationId,
            IsSystem = isSystem,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordHash = null,
            SecurityStamp = Guid.NewGuid()
        };
    }

    public void ApplyEntraMapping(Role role, Guid organizationId)
    {
        EnsureMutable();
        Role = CapToMaxSsoAssignableRole(role);
        OrganizationId = organizationId;
        RotateSecurityStamp();
    }

    private static Role CapToMaxSsoAssignableRole(Role role)
        => role > MaxSsoAssignableRole ? MaxSsoAssignableRole : role;

    public static User CreateWithPassword(
        string email,
        string username,
        string passwordHash,
        Role role,
        bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new BadRequestException("Username must not be empty");

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Username = username,
            PasswordHash = passwordHash,
            Role = role,
            IsSystem = isSystem,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            EntraObjectId = null,
            SecurityStamp = Guid.NewGuid()
        };
    }
}
