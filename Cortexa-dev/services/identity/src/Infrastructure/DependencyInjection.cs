using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Persistence;
using Cortexa.Identity.Infrastructure.Repositories;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cortexa.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString)
                .AddInterceptors(new AuditImmutabilityInterceptor()));

        services.AddDbContextFactory<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString)
                .AddInterceptors(new AuditImmutabilityInterceptor()));

        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<EntraIdSettings>(configuration.GetSection("EntraId"));
        services.Configure<SeedSettings>(configuration.GetSection("Seed"));
        services.Configure<PermissionSeedSettings>(configuration.GetSection("Permissions"));
        services.Configure<LockoutSettings>(configuration.GetSection("Lockout"));
        services.Configure<InternalApiSettings>(configuration.GetSection("Internal"));

        services.AddHttpContextAccessor();

        services.AddScoped<AdminSeeder>();
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IFailedLoginRepository, FailedLoginRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<ICurrentActor, HttpContextCurrentActor>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IEntraTokenValidator, EntraTokenValidator>();
        services.AddSingleton<IEntraGroupMapper, EntraGroupMapper>();

        return services;
    }
}
