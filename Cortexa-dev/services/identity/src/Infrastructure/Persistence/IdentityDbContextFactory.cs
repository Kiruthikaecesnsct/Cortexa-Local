using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cortexa.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CORTEXA_IDENTITY_DB")
            ?? throw new InvalidOperationException("CORTEXA_IDENTITY_DB environment variable is not set.");

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}
