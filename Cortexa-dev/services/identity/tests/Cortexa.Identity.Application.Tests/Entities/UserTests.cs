using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Entities;

public sealed class UserTests
{
    [Fact]
    public void EnsureMutable_WhenUserIsSystem_ThrowsImmutableUserException()
    {
        var user = User.CreateWithPassword(
            email: "system@cortexa.io",
            username: "system-user",
            passwordHash: "hashed-password",
            role: Role.Admin,
            isSystem: true);

        Assert.Throws<ImmutableUserException>(user.EnsureMutable);
    }

    [Fact]
    public void EnsureMutable_WhenUserIsNotSystem_DoesNotThrow()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        var exception = Record.Exception(user.EnsureMutable);

        Assert.Null(exception);
    }

    [Fact]
    public void AssignOrganization_WhenUserIsSystem_ThrowsImmutableUserException()
    {
        var organizationId = Guid.NewGuid();
        var user = User.CreateWithPassword(
            email: "superadmin@cortexa.io",
            username: "superadmin",
            passwordHash: "hashed-password",
            role: Role.SuperAdmin,
            isSystem: true);

        Assert.Throws<ImmutableUserException>(() => user.AssignOrganization(organizationId));
        Assert.Null(user.OrganizationId);
    }

    [Fact]
    public void AssignOrganization_WhenUserIsNotSystem_SetsOrganizationId()
    {
        var organizationId = Guid.NewGuid();
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        user.AssignOrganization(organizationId);

        Assert.Equal(organizationId, user.OrganizationId);
    }

    [Fact]
    public void Disable_WhenUserIsSystem_ThrowsImmutableUserException()
    {
        var user = User.CreateWithPassword(
            email: "system@cortexa.io",
            username: "system-user",
            passwordHash: "hashed-password",
            role: Role.Admin,
            isSystem: true);

        Assert.Throws<ImmutableUserException>(user.Disable);
    }

    [Fact]
    public void Disable_WhenUserIsNotSystem_SetsIsEnabledFalse()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        user.Disable();

        Assert.False(user.IsEnabled);
    }

    [Fact]
    public void Enable_WhenUserIsSystem_SetsIsEnabledTrue()
    {
        var user = User.CreateWithPassword(
            email: "system@cortexa.io",
            username: "system-user",
            passwordHash: "hashed-password",
            role: Role.Admin,
            isSystem: true);

        user.Enable();

        Assert.True(user.IsEnabled);
    }

    [Fact]
    public void Enable_WhenUserIsNotSystem_SetsIsEnabledTrue()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        user.Enable();

        Assert.True(user.IsEnabled);
    }

    [Fact]
    public void SetRole_WhenUserIsSystem_ThrowsImmutableUserException()
    {
        var user = User.CreateWithPassword(
            email: "system@cortexa.io",
            username: "system-user",
            passwordHash: "hashed-password",
            role: Role.Admin,
            isSystem: true);

        Assert.Throws<ImmutableUserException>(() => user.SetRole(Role.Researcher));
    }

    [Fact]
    public void SetRole_WhenRoleIsSuperAdmin_ThrowsBadRequestException()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        Assert.Throws<BadRequestException>(() => user.SetRole(Role.SuperAdmin));
    }

    [Fact]
    public void SetRole_WhenRoleIsValid_SetsRole()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher,
            isSystem: false);

        user.SetRole(Role.Admin);

        Assert.Equal(Role.Admin, user.Role);
    }

    [Fact]
    public void CreateFromEntra_RoleIsSuperAdmin_CapsRoleToAdmin()
    {
        var organizationId = Guid.NewGuid();

        var user = User.CreateFromEntra(
            email: "sso@cortexa.io",
            username: "sso-user",
            entraObjectId: "entra-oid-1",
            role: Role.SuperAdmin,
            organizationId: organizationId);

        Assert.Equal(Role.Admin, user.Role);
        Assert.NotEqual(Role.SuperAdmin, user.Role);
    }

    [Fact]
    public void CreateFromEntra_RoleBelowCap_KeepsRequestedRoleAndOrganization()
    {
        var organizationId = Guid.NewGuid();

        var user = User.CreateFromEntra(
            email: "sso@cortexa.io",
            username: "sso-user",
            entraObjectId: "entra-oid-1",
            role: Role.Reviewer,
            organizationId: organizationId);

        Assert.Equal(Role.Reviewer, user.Role);
        Assert.Equal(organizationId, user.OrganizationId);
    }

    [Fact]
    public void ApplyEntraMapping_RoleIsSuperAdmin_CapsRoleToAdmin()
    {
        var organizationId = Guid.NewGuid();
        var user = User.CreateFromEntra(
            email: "sso@cortexa.io",
            username: "sso-user",
            entraObjectId: "entra-oid-1",
            role: Role.Researcher,
            organizationId: Guid.NewGuid());

        user.ApplyEntraMapping(Role.SuperAdmin, organizationId);

        Assert.Equal(Role.Admin, user.Role);
        Assert.NotEqual(Role.SuperAdmin, user.Role);
        Assert.Equal(organizationId, user.OrganizationId);
    }

    [Fact]
    public void ApplyEntraMapping_WhenUserIsSystem_ThrowsImmutableUserException()
    {
        var user = User.CreateFromEntra(
            email: "system@cortexa.io",
            username: "system-user",
            entraObjectId: "entra-oid-system",
            role: Role.Admin,
            organizationId: Guid.NewGuid(),
            isSystem: true);
        var originalRole = user.Role;
        var originalOrganizationId = user.OrganizationId;

        Assert.Throws<ImmutableUserException>(
            () => user.ApplyEntraMapping(Role.Admin, Guid.NewGuid()));
        Assert.Equal(originalRole, user.Role);
        Assert.Equal(originalOrganizationId, user.OrganizationId);
    }

    [Fact]
    public void ApplyEntraMapping_WhenUserIsNotSystem_UpdatesRoleAndOrganization()
    {
        var newOrganizationId = Guid.NewGuid();
        var user = User.CreateFromEntra(
            email: "sso@cortexa.io",
            username: "sso-user",
            entraObjectId: "entra-oid-1",
            role: Role.Researcher,
            organizationId: Guid.NewGuid());

        user.ApplyEntraMapping(Role.Admin, newOrganizationId);

        Assert.Equal(Role.Admin, user.Role);
        Assert.Equal(newOrganizationId, user.OrganizationId);
    }

    [Fact]
    public void CreateWithPassword_AssignsNonEmptySecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);

        Assert.NotEqual(Guid.Empty, user.SecurityStamp);
    }

    [Fact]
    public void Disable_RotatesSecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);
        var previousStamp = user.SecurityStamp;

        user.Disable();

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }

    [Fact]
    public void SetRole_RotatesSecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);
        var previousStamp = user.SecurityStamp;

        user.SetRole(Role.Admin);

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }

    [Fact]
    public void ChangePassword_RotatesSecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);
        var previousStamp = user.SecurityStamp;

        user.ChangePassword("new-hashed-password");

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }

    [Fact]
    public void ApplyEntraMapping_WhenUserIsNotSystem_RotatesSecurityStamp()
    {
        var user = User.CreateFromEntra(
            email: "sso@cortexa.io",
            username: "sso-user",
            entraObjectId: "entra-oid-1",
            role: Role.Researcher,
            organizationId: Guid.NewGuid());
        var previousStamp = user.SecurityStamp;

        user.ApplyEntraMapping(Role.Admin, Guid.NewGuid());

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }

    [Fact]
    public void Enable_DoesNotRotateSecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);
        user.Disable();
        var stampAfterDisable = user.SecurityStamp;

        user.Enable();

        Assert.Equal(stampAfterDisable, user.SecurityStamp);
    }

    [Fact]
    public void InvalidateSessions_RotatesSecurityStamp()
    {
        var user = User.CreateWithPassword(
            email: "regular@cortexa.io",
            username: "regular-user",
            passwordHash: "hashed-password",
            role: Role.Researcher);
        var previousStamp = user.SecurityStamp;

        user.InvalidateSessions();

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }
}
