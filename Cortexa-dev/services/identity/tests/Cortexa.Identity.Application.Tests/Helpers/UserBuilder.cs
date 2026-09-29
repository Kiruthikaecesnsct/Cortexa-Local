using System.Reflection;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Tests.Helpers;

internal static class UserBuilder
{
    internal static User Build(
        Guid? id = null,
        string email = "test@example.com",
        string username = "testuser",
        string? passwordHash = null,
        Role role = Role.Researcher,
        string? entraObjectId = null,
        bool isSystem = false,
        bool isEnabled = true,
        Guid? organizationId = null,
        Guid? securityStamp = null)
    {
        var user = (User)Activator.CreateInstance(typeof(User), nonPublic: true)!;

        SetProperty(user, nameof(User.Id), id ?? Guid.NewGuid());
        SetProperty(user, nameof(User.Email), email);
        SetProperty(user, nameof(User.Username), username);
        SetProperty(user, nameof(User.PasswordHash), passwordHash);
        SetProperty(user, nameof(User.Role), role);
        SetProperty(user, nameof(User.EntraObjectId), entraObjectId);
        SetProperty(user, nameof(User.IsSystem), isSystem);
        SetProperty(user, nameof(User.IsEnabled), isEnabled);
        SetProperty(user, nameof(User.CreatedAt), DateTimeOffset.UtcNow);
        SetProperty(user, nameof(User.OrganizationId), organizationId);
        SetProperty(user, nameof(User.SecurityStamp), securityStamp ?? Guid.NewGuid());

        return user;
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = typeof(User).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        var backingField = typeof(User).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (backingField is not null)
            backingField.SetValue(target, value);
        else
            property.SetValue(target, value);
    }
}
