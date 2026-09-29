using Cortexa.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cortexa.Identity.Infrastructure.Persistence;

public static class MigrationRunner
{
    public static async Task MigrateAndSeedAsync(
        IServiceProvider services,
        string adminPassword,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Identity migrations applied.");

            var seeder = scope.ServiceProvider.GetRequiredService<AdminSeeder>();
            await seeder.SeedAsync(adminPassword, ct);

            var permissionSeeder = scope.ServiceProvider.GetRequiredService<PermissionSeeder>();
            await permissionSeeder.SeedAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identity startup migration/seed failed.");
            throw;
        }
    }
}
