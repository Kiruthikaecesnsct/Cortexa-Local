using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Infrastructure;

public sealed class OrganizationConfigurationTests
{
    [Fact]
    public void OrganizationModel_NameIndex_IsUniqueAndScopedToActiveRows()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=cortexa_identity_model_test")
            .Options;

        using var context = new IdentityDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(Organization));
        Assert.NotNull(entityType);

        IIndex nameIndex = entityType!.GetIndexes()
            .Single(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(Organization.Name));

        Assert.True(nameIndex.IsUnique);
        Assert.Equal("deleted_at IS NULL", nameIndex.GetFilter());
    }

    [Fact]
    public async Task Organizations_TwoSimultaneouslyActiveOrgs_WithSameName_ViolateUniqueIndexAtDatabaseLevel()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new IdentityDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var activeOrg = Organization.Create(Guid.NewGuid(), "Acme Corp");
        await using (var firstContext = new IdentityDbContext(options))
        {
            firstContext.Organizations.Add(activeOrg);
            await firstContext.SaveChangesAsync();
        }

        var duplicateActiveOrg = Organization.Create(Guid.NewGuid(), "Acme Corp");
        await using (var secondContext = new IdentityDbContext(options))
        {
            secondContext.Organizations.Add(duplicateActiveOrg);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Organizations_NewOrg_CanReuseName_OfSoftDeletedOrg()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new IdentityDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var deletedOrg = Organization.Create(Guid.NewGuid(), "Acme Corp");
        deletedOrg.SoftDelete();
        await using (var firstContext = new IdentityDbContext(options))
        {
            firstContext.Organizations.Add(deletedOrg);
            await firstContext.SaveChangesAsync();
        }

        var reusedNameOrg = Organization.Create(Guid.NewGuid(), "Acme Corp");
        await using (var secondContext = new IdentityDbContext(options))
        {
            secondContext.Organizations.Add(reusedNameOrg);
            var exception = await Record.ExceptionAsync(() => secondContext.SaveChangesAsync());
            Assert.Null(exception);
        }
    }
}
