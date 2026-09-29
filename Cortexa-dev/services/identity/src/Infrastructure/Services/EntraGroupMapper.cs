using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class EntraGroupMapper : IEntraGroupMapper
{
    private const Role MaxSsoAssignableRole = Role.Admin;

    private readonly Dictionary<string, GroupMapping> _groupMappings;
    private readonly Guid _defaultOrganizationId;

    public EntraGroupMapper(IOptions<EntraIdSettings> settings)
    {
        _groupMappings = settings.Value.GroupMappings;
        _defaultOrganizationId = settings.Value.DefaultOrganizationId;
    }

    public EntraAssignment Map(IReadOnlyList<string> groupIds)
    {
        var role = ResolveHighestRole(groupIds);
        var organizationId = ResolveOrganization(groupIds);
        return new EntraAssignment(role, organizationId);
    }

    private Role ResolveHighestRole(IReadOnlyList<string> groupIds)
    {
        var highestRole = Role.Researcher;

        foreach (var groupId in groupIds)
        {
            if (!_groupMappings.TryGetValue(groupId, out var mapping))
                continue;

            if (mapping.Role > highestRole)
                highestRole = mapping.Role;
        }

        return CapToMaxAssignableRole(highestRole);
    }

    private Guid ResolveOrganization(IReadOnlyList<string> groupIds)
    {
        foreach (var groupId in groupIds)
        {
            if (_groupMappings.TryGetValue(groupId, out var mapping))
                return mapping.OrganizationId;
        }

        return _defaultOrganizationId;
    }

    private static Role CapToMaxAssignableRole(Role role)
        => role > MaxSsoAssignableRole ? MaxSsoAssignableRole : role;
}
