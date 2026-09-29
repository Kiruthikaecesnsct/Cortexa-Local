using Cortexa.Identity.Domain;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Infrastructure.Configuration;

public sealed class GroupMapping
{
    public Role Role { get; set; }
    public Guid OrganizationId { get; set; }
}

public sealed class EntraIdSettings
{
    public string MetadataAddress { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ValidIssuer { get; set; } = string.Empty;
    public Dictionary<string, GroupMapping> GroupMappings { get; set; } = new();
    public Guid DefaultOrganizationId { get; set; } = OrganizationConstants.DefaultOrganizationId;
}
