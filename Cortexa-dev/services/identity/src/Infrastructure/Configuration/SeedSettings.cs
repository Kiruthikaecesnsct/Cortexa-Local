namespace Cortexa.Identity.Infrastructure.Configuration;

public sealed class SeedSettings
{
    public string AdminEmail { get; init; } = string.Empty;
    public string AdminUsername { get; init; } = "admin";
    public string AdminEmailSecretName { get; init; } = "identity-admin-email";
    public string AdminPasswordSecretName { get; init; } = "identity-admin-initial-password";
    public string SuperAdminEmail { get; init; } = "superadmin@cortexa.co";
    public string SuperAdminUsername { get; init; } = "superadmin";
}
