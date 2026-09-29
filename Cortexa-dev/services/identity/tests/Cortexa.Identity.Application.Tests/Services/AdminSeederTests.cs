using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Npgsql;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Services;

public sealed class AdminSeederTests
{
    private const string LegacyAdminEmail = "admin@cortexa.io";
    private const string LegacyAdminUsername = "cortexa-admin";
    private const string SuperAdminEmail = "superadmin@cortexa.co";
    private const string SuperAdminUsername = "superadmin";
    private const string AdminPassword = "Adm!n$ecure123";

    private readonly IUserRepository _userRepo;

    public AdminSeederTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
    }

    private AdminSeeder BuildSeeder(
        string adminEmail = LegacyAdminEmail,
        string adminUsername = LegacyAdminUsername,
        string superAdminEmail = SuperAdminEmail,
        string superAdminUsername = SuperAdminUsername)
    {
        var settings = Options.Create(new SeedSettings
        {
            AdminEmail = adminEmail,
            AdminUsername = adminUsername,
            SuperAdminEmail = superAdminEmail,
            SuperAdminUsername = superAdminUsername
        });

        return new AdminSeeder(
            _userRepo,
            settings,
            NullLogger<AdminSeeder>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedAsync_SuperAdminEmailNotConfigured_ReturnsWithoutCallingRepository(string superAdminEmail)
    {
        var seeder = BuildSeeder(superAdminEmail: superAdminEmail);

        await seeder.SeedAsync(AdminPassword, CancellationToken.None);

        _ = _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _ = _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedAsync_PasswordNotConfigured_ReturnsWithoutCallingRepository(string adminPassword)
    {
        var seeder = BuildSeeder();

        await seeder.SeedAsync(adminPassword, CancellationToken.None);

        _ = _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _ = _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_SuperAdminAlreadyExists_DoesNotPromoteOrCreate()
    {
        var existingSuperAdmin = UserBuilder.Build(email: SuperAdminEmail, role: Role.SuperAdmin);
        _userRepo.GetByEmailAsync(SuperAdminEmail, Arg.Any<CancellationToken>())
            .Returns(existingSuperAdmin);

        var seeder = BuildSeeder();

        await seeder.SeedAsync(AdminPassword, CancellationToken.None);

        _ = _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        _ = _userRepo.DidNotReceive().PromoteToSuperAdminAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_LegacyAdminExists_PromotesInPlace()
    {
        var legacyAdmin = UserBuilder.Build(email: LegacyAdminEmail, role: Role.Admin);
        _userRepo.GetByEmailAsync(SuperAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.GetByEmailAsync(LegacyAdminEmail, Arg.Any<CancellationToken>())
            .Returns(legacyAdmin);
        _userRepo.PromoteToSuperAdminAsync(
                LegacyAdminEmail, SuperAdminEmail, SuperAdminUsername, Arg.Any<CancellationToken>())
            .Returns(true);

        var seeder = BuildSeeder();

        await seeder.SeedAsync(AdminPassword, CancellationToken.None);

        _ = await _userRepo.Received(1).PromoteToSuperAdminAsync(
            LegacyAdminEmail, SuperAdminEmail, SuperAdminUsername, Arg.Any<CancellationToken>());
        _ = _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoSuperAdminAndNoLegacyAdmin_CreatesSuperAdminWithCorrectProperties()
    {
        User? captured = null;
        _userRepo.GetByEmailAsync(SuperAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.GetByEmailAsync(LegacyAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.AddAsync(Arg.Do<User>(u => captured = u), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var seeder = BuildSeeder();

        await seeder.SeedAsync(AdminPassword, CancellationToken.None);

        _ = _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(SuperAdminEmail, captured!.Email);
        Assert.Equal(SuperAdminUsername, captured.Username);
        Assert.Equal(Role.SuperAdmin, captured.Role);
        Assert.True(captured.IsSystem);
        Assert.NotNull(captured.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(AdminPassword, captured.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_ConcurrentCreateRace_UniqueViolationOnAdd_CompletesWithoutThrowing()
    {
        const string uniqueViolationMessage = "23505: duplicate key value violates unique constraint";
        _userRepo.GetByEmailAsync(SuperAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.GetByEmailAsync(LegacyAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new DbUpdateException(
                "duplicate key",
                new Exception(uniqueViolationMessage)));

        var seeder = BuildSeeder();

        var exception = await Record.ExceptionAsync(
            () => seeder.SeedAsync(AdminPassword, CancellationToken.None));

        Assert.Null(exception);
        _ = _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_ConcurrentPromoteRace_UniqueViolationOnPromote_CompletesWithoutThrowing()
    {
        var legacyAdmin = UserBuilder.Build(email: LegacyAdminEmail, role: Role.Admin);
        var uniqueViolation = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            "23505");
        _userRepo.GetByEmailAsync(SuperAdminEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.GetByEmailAsync(LegacyAdminEmail, Arg.Any<CancellationToken>())
            .Returns(legacyAdmin);
        _userRepo.PromoteToSuperAdminAsync(
                LegacyAdminEmail, SuperAdminEmail, SuperAdminUsername, Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw uniqueViolation);

        var seeder = BuildSeeder();

        var exception = await Record.ExceptionAsync(
            () => seeder.SeedAsync(AdminPassword, CancellationToken.None));

        Assert.Null(exception);
        _ = await _userRepo.Received(1).PromoteToSuperAdminAsync(
            LegacyAdminEmail, SuperAdminEmail, SuperAdminUsername, Arg.Any<CancellationToken>());
        _ = _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }
}
