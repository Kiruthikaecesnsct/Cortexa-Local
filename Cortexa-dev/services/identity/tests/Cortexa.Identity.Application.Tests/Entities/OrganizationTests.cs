using Cortexa.Identity.Domain.Entities;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Entities;

public sealed class OrganizationTests
{
    private static readonly Guid FixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_WithNameAndId_SetsAllPropertiesAndNotDeleted()
    {
        var organization = Organization.Create(FixedId, "Acme Corp");

        Assert.Equal(FixedId, organization.Id);
        Assert.Equal("Acme Corp", organization.Name);
        Assert.False(organization.IsDeleted);
        Assert.Null(organization.DeletedAt);
        Assert.True(organization.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.True(organization.UpdatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_WithoutId_GeneratesNonEmptyId()
    {
        var organization = Organization.Create("Acme Corp");

        Assert.NotEqual(Guid.Empty, organization.Id);
    }

    [Fact]
    public void SoftDelete_SetsDeletedAtAndIsDeletedTrue()
    {
        var organization = Organization.Create(FixedId, "Acme Corp");

        organization.SoftDelete();

        Assert.True(organization.IsDeleted);
        Assert.NotNull(organization.DeletedAt);
    }

    [Fact]
    public void Rename_UpdatesNameAndUpdatedAt()
    {
        var organization = Organization.Create(FixedId, "Acme Corp");
        var originalUpdatedAt = organization.UpdatedAt;

        organization.Rename("New Name");

        Assert.Equal("New Name", organization.Name);
        Assert.True(organization.UpdatedAt >= originalUpdatedAt);
    }
}
