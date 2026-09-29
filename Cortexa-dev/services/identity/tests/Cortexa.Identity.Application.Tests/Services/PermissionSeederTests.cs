using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Persistence;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Services;

public sealed class PermissionSeederTests : IAsyncLifetime
{
    private const string PermRead = "documents.read";
    private const string PermWrite = "documents.write";
    private const string PermDelete = "documents.delete";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<IdentityDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var context = new IdentityDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private IdentityDbContext NewContext() => new(_options);

    private static PermissionSeeder BuildSeeder(IdentityDbContext context, PermissionSeedSettings settings)
        => new(context, Options.Create(settings), NullLogger<PermissionSeeder>.Instance);

    private static PermissionSeedSettings FullCatalog() => new()
    {
        Catalog =
        [
            new PermissionEntry { Name = PermRead, Description = "Read" },
            new PermissionEntry { Name = PermWrite, Description = "Write" },
            new PermissionEntry { Name = PermDelete, Description = "Delete" }
        ],
        DefaultRolePermissions = new Dictionary<string, List<string>>
        {
            [nameof(Role.Admin)] = [PermRead, PermWrite, PermDelete],
            [nameof(Role.Researcher)] = [PermRead]
        }
    };

    [Fact]
    public async Task SeedAsync_EmptyDatabase_InsertsAllPermissionsAndRolePermissions()
    {
        await using (var context = NewContext())
        {
            await BuildSeeder(context, FullCatalog()).SeedAsync(CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.Equal(3, await verify.Permissions.CountAsync());
        Assert.Equal(4, await verify.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_RunTwice_IsIdempotent_NoDuplicates()
    {
        await using (var context = NewContext())
        {
            await BuildSeeder(context, FullCatalog()).SeedAsync(CancellationToken.None);
        }

        // Second run is the deployed hot path: every row already exists.
        await using (var context = NewContext())
        {
            await BuildSeeder(context, FullCatalog()).SeedAsync(CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.Equal(3, await verify.Permissions.CountAsync());
        Assert.Equal(4, await verify.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_SomePermissionsExist_InsertsOnlyMissing()
    {
        await using (var context = NewContext())
        {
            context.Permissions.Add(Permission.Create(PermRead, "Read"));
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            await BuildSeeder(context, FullCatalog()).SeedAsync(CancellationToken.None);
        }

        await using var verify = NewContext();
        var names = await verify.Permissions.Select(p => p.Name).ToListAsync();
        Assert.Equal(3, names.Count);
        Assert.Contains(PermRead, names);
        Assert.Contains(PermWrite, names);
        Assert.Contains(PermDelete, names);
    }

    [Fact]
    public async Task SeedAsync_UnknownRole_SkipsWithoutInsertingRolePermissions()
    {
        var settings = new PermissionSeedSettings
        {
            Catalog = [new PermissionEntry { Name = PermRead, Description = "Read" }],
            DefaultRolePermissions = new Dictionary<string, List<string>>
            {
                ["NotARole"] = [PermRead]
            }
        };

        await using (var context = NewContext())
        {
            await BuildSeeder(context, settings).SeedAsync(CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Permissions.CountAsync());
        Assert.Equal(0, await verify.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_ConcurrentWinnerInsertedSubsetMidFlight_FillsRemainingWithoutDropping()
    {
        // Simulate the rolling-deploy race: another replica ("winner") inserts a
        // subset of the catalog after this seeder computed its plan but before it
        // committed. A commit conflict must recompute and still insert the rest,
        // never silently drop the non-conflicting rows.
        var settings = FullCatalog();

        await using var context = NewContext();
        var seeder = BuildSeeder(context, settings);

        // Winner races in on a separate connection/context, seeding PermRead only.
        await using (var winner = NewContext())
        {
            winner.Permissions.Add(Permission.Create(PermRead, "Read"));
            await winner.SaveChangesAsync();
        }

        await seeder.SeedAsync(CancellationToken.None);

        await using var verify = NewContext();
        var names = await verify.Permissions.Select(p => p.Name).ToListAsync();
        Assert.Equal(3, names.Count);
        Assert.Contains(PermWrite, names);
        Assert.Contains(PermDelete, names);
        Assert.Equal(4, await verify.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_RolePermissionReferencesMissingPermission_SkipsThatPairOnly()
    {
        var settings = new PermissionSeedSettings
        {
            Catalog = [new PermissionEntry { Name = PermRead, Description = "Read" }],
            DefaultRolePermissions = new Dictionary<string, List<string>>
            {
                [nameof(Role.Admin)] = [PermRead, "ghost.permission"]
            }
        };

        await using (var context = NewContext())
        {
            await BuildSeeder(context, settings).SeedAsync(CancellationToken.None);
        }

        await using var verify = NewContext();
        var rolePerms = await verify.RolePermissions.ToListAsync();
        Assert.Single(rolePerms);
        Assert.Equal(Role.Admin, rolePerms[0].Role);
    }
}
