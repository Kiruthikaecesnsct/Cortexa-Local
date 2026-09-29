using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class AdminSeeder
{
    private const int BcryptWorkFactor = 11;
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly IUserRepository _userRepo;
    private readonly SeedSettings _settings;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        IUserRepository userRepo,
        IOptions<SeedSettings> settings,
        ILogger<AdminSeeder> logger)
    {
        _userRepo = userRepo;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(string adminPassword, CancellationToken ct)
    {
        var superAdminEmail = _settings.SuperAdminEmail;

        if (string.IsNullOrWhiteSpace(superAdminEmail))
        {
            _logger.LogWarning("Seed:SuperAdminEmail is not configured. Skipping superadmin seed.");
            return;
        }

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogWarning("Admin password secret not found. Skipping superadmin seed.");
            return;
        }

        var existingSuperAdmin = await _userRepo.GetByEmailAsync(superAdminEmail, ct);

        if (existingSuperAdmin is not null)
        {
            _logger.LogInformation("Superadmin user already exists, skipping seed.");
            return;
        }

        if (await TryPromoteLegacyAdminAsync(superAdminEmail, ct))
            return;

        await CreateSuperAdminAsync(superAdminEmail, adminPassword, ct);
    }

    private async Task<bool> TryPromoteLegacyAdminAsync(string superAdminEmail, CancellationToken ct)
    {
        var legacyEmail = _settings.AdminEmail;

        if (string.IsNullOrWhiteSpace(legacyEmail))
            return false;

        var legacyAdmin = await _userRepo.GetByEmailAsync(legacyEmail, ct);

        if (legacyAdmin is null)
            return false;

        try
        {
            var promoted = await _userRepo.PromoteToSuperAdminAsync(
                legacyEmail,
                superAdminEmail,
                _settings.SuperAdminUsername,
                ct);

            _logger.LogInformation(
                promoted
                    ? "Legacy admin promoted to superadmin."
                    : "Legacy admin already promoted by a concurrent instance.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresUniqueViolationSqlState)
        {
            _logger.LogInformation("Superadmin seeded by a concurrent instance during promotion. Skipping.");
        }

        return true;
    }

    private async Task CreateSuperAdminAsync(string superAdminEmail, string adminPassword, CancellationToken ct)
    {
        try
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, BcryptWorkFactor);
            var superAdmin = User.CreateWithPassword(
                superAdminEmail,
                _settings.SuperAdminUsername,
                passwordHash,
                Role.SuperAdmin,
                isSystem: true);

            await _userRepo.AddAsync(superAdmin, ct);
            _logger.LogInformation("Superadmin user seeded successfully.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(PostgresUniqueViolationSqlState) == true)
        {
            _logger.LogInformation("Superadmin user seeded by a concurrent instance. Skipping.");
        }
    }
}
