namespace Cortexa.Identity.Domain.Enums;

public enum AuditEventType
{
    UserCreated,
    UserUpdated,
    UserDisabled,
    UserEnabled,
    UserDeleted,
    UserRoleChanged,
    UserPasswordChanged,
    RolePermissionsChanged,
    OrganizationChanged,
    LoginSucceeded,
    LoginFailed,
    Logout,
    TokenRefreshed,
    TokenRefreshFailed,
    TokenRevoked,
    UserUnlocked
}
