namespace Cortexa.Identity.Infrastructure.Configuration;

public sealed class PermissionSeedSettings
{
    public List<PermissionEntry> Catalog { get; init; } = [];
    public Dictionary<string, List<string>> DefaultRolePermissions { get; init; } = [];
}

public sealed class PermissionEntry
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
