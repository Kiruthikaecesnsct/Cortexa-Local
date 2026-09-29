using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Services;

public sealed class EntraGroupMapperTests
{
    private const string ResearcherGroupId = "grp-researcher";
    private const string AdminGroupId = "grp-admin";
    private const string SuperAdminGroupId = "grp-superadmin";
    private const string UnknownGroupId = "grp-unknown";
    private static readonly Guid DefaultOrgId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid MappedOrgId = new("11111111-1111-1111-1111-111111111111");

    private static EntraGroupMapper BuildMapper(Dictionary<string, GroupMapping> groupMappings)
    {
        var settings = Options.Create(new EntraIdSettings
        {
            GroupMappings = groupMappings,
            DefaultOrganizationId = DefaultOrgId
        });

        return new EntraGroupMapper(settings);
    }

    [Fact]
    public void Map_NoMatchingGroups_ReturnsResearcherAndDefaultOrg()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(new[] { UnknownGroupId });

        Assert.Equal(Role.Researcher, result.Role);
        Assert.Equal(DefaultOrgId, result.OrganizationId);
    }

    [Fact]
    public void Map_MultipleMatchingGroups_ReturnsHighestMappedRole()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [ResearcherGroupId] = new GroupMapping { Role = Role.Researcher, OrganizationId = DefaultOrgId },
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(new[] { ResearcherGroupId, AdminGroupId });

        Assert.Equal(Role.Admin, result.Role);
    }

    [Fact]
    public void Map_GroupMappedToSuperAdmin_ClampsToAdmin()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [SuperAdminGroupId] = new GroupMapping { Role = Role.SuperAdmin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(new[] { SuperAdminGroupId });

        Assert.Equal(Role.Admin, result.Role);
        Assert.NotEqual(Role.SuperAdmin, result.Role);
    }

    [Fact]
    public void Map_SuperAdminGroupMixedWithLowerGroups_StillClampsToAdmin()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [ResearcherGroupId] = new GroupMapping { Role = Role.Researcher, OrganizationId = DefaultOrgId },
            [SuperAdminGroupId] = new GroupMapping { Role = Role.SuperAdmin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(new[] { ResearcherGroupId, SuperAdminGroupId });

        Assert.Equal(Role.Admin, result.Role);
    }

    [Fact]
    public void Map_FirstMatchingGroupInList_ResolvesOrganization()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [ResearcherGroupId] = new GroupMapping { Role = Role.Researcher, OrganizationId = MappedOrgId },
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = DefaultOrgId }
        });

        var result = mapper.Map(new[] { ResearcherGroupId, AdminGroupId });

        Assert.Equal(MappedOrgId, result.OrganizationId);
    }

    [Fact]
    public void Map_SingleMappedGroup_ReturnsBothMappedRoleAndOrgTogether()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(new[] { AdminGroupId });

        Assert.Equal(Role.Admin, result.Role);
        Assert.Equal(MappedOrgId, result.OrganizationId);
    }

    [Fact]
    public void Map_MultipleGroupsMappedToDifferentOrgs_OrgResolutionFollowsGroupListOrderNotDictionaryOrder()
    {
        var thirdOrgId = new Guid("22222222-2222-2222-2222-222222222222");
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [ResearcherGroupId] = new GroupMapping { Role = Role.Researcher, OrganizationId = DefaultOrgId },
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = MappedOrgId },
            [SuperAdminGroupId] = new GroupMapping { Role = Role.Reviewer, OrganizationId = thirdOrgId }
        });

        var resultAdminFirst = mapper.Map(new[] { AdminGroupId, SuperAdminGroupId });
        var resultSuperAdminGroupFirst = mapper.Map(new[] { SuperAdminGroupId, AdminGroupId });

        Assert.Equal(MappedOrgId, resultAdminFirst.OrganizationId);
        Assert.Equal(thirdOrgId, resultSuperAdminGroupFirst.OrganizationId);
    }

    [Fact]
    public void Map_EmptyGroupList_ReturnsResearcherAndDefaultOrg()
    {
        var mapper = BuildMapper(new Dictionary<string, GroupMapping>
        {
            [AdminGroupId] = new GroupMapping { Role = Role.Admin, OrganizationId = MappedOrgId }
        });

        var result = mapper.Map(Array.Empty<string>());

        Assert.Equal(Role.Researcher, result.Role);
        Assert.Equal(DefaultOrgId, result.OrganizationId);
    }
}
