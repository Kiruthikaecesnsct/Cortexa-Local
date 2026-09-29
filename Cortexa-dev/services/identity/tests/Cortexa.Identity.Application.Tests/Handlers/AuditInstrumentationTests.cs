using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class AuditInstrumentationTests
{
    private const string ValidEmail = "alpha@cortexa.io";
    private const string ValidPassword = "C0rr3ctP@ssword!";
    private const string WrongPassword = "wr0ngP@ssword!";

    [Fact]
    public async Task LoginHandler_WrongPassword_LogsLoginFailedWithReasonAndNoCredentials()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var permissionRepo = Substitute.For<IPermissionRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var failedLoginRepo = Substitute.For<IFailedLoginRepository>();
        failedLoginRepo.RegisterFailureAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new LockState(1, null));

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new LoginHandler(
            userRepo, refreshTokenRepo, tokenService, permissionRepo, auditWriter, failedLoginRepo);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.HandleAsync(new LoginRequest(ValidEmail, WrongPassword), CancellationToken.None));

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.LoginFailed,
            user.Id,
            "login",
            false,
            "wrong-password",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginHandler_UnknownEmail_LogsLoginFailedWithNullUserIdAndUserNotFoundReason()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var permissionRepo = Substitute.For<IPermissionRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var failedLoginRepo = Substitute.For<IFailedLoginRepository>();

        userRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = new LoginHandler(
            userRepo, refreshTokenRepo, tokenService, permissionRepo, auditWriter, failedLoginRepo);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.HandleAsync(new LoginRequest("ghost@cortexa.io", ValidPassword), CancellationToken.None));

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.LoginFailed,
            null,
            "login",
            false,
            "user-not-found",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginHandler_ValidCredentials_LogsLoginSucceeded()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var permissionRepo = Substitute.For<IPermissionRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var failedLoginRepo = Substitute.For<IFailedLoginRepository>();

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>()).Returns(user);
        permissionRepo.GetPermissionNamesForUserAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>())
            .Returns(("access-token", DateTimeOffset.UtcNow.AddHours(1)));
        tokenService.GenerateRefreshToken().Returns(("raw", "hash"));

        var handler = new LoginHandler(
            userRepo, refreshTokenRepo, tokenService, permissionRepo, auditWriter, failedLoginRepo);

        await handler.HandleAsync(new LoginRequest(ValidEmail, ValidPassword), CancellationToken.None);

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.LoginSucceeded,
            user.Id,
            "login",
            true,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutHandler_ActiveToken_LogsLogoutAndTokenRevoked()
    {
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var auditWriter = Substitute.For<IAuditWriter>();

        var userId = Guid.NewGuid();
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: "hash");
        tokenService.ComputeTokenHash("raw").Returns("hash");
        refreshTokenRepo.GetByTokenHashAsync("hash", Arg.Any<CancellationToken>()).Returns(token);

        var handler = new LogoutHandler(refreshTokenRepo, tokenService, auditWriter);

        await handler.HandleAsync("raw", CancellationToken.None);

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.Logout, userId, "logout", true, null, Arg.Any<CancellationToken>());
        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.TokenRevoked, userId, "logout", true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshHandler_ValidToken_LogsTokenRefreshed()
    {
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var userRepo = Substitute.For<IUserRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var permissionRepo = Substitute.For<IPermissionRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();

        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: "hash");

        tokenService.ComputeTokenHash("raw").Returns("hash");
        refreshTokenRepo.GetByTokenHashAsync("hash", Arg.Any<CancellationToken>()).Returns(token);
        userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        permissionRepo.GetPermissionNamesForUserAsync(Arg.Any<Role>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>())
            .Returns(("access-token", DateTimeOffset.UtcNow.AddHours(1)));
        tokenService.GenerateRefreshToken().Returns(("new-raw", "new-hash"));

        var handler = new RefreshHandler(refreshTokenRepo, userRepo, tokenService, permissionRepo, auditWriter);

        await handler.HandleAsync("raw", CancellationToken.None);

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.TokenRefreshed, userId, "refresh", true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshHandler_RevokedToken_LogsTokenRefreshFailedAndTokenRevoked()
    {
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var userRepo = Substitute.For<IUserRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var permissionRepo = Substitute.For<IPermissionRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();

        var userId = Guid.NewGuid();
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: "hash", revoked: true);

        tokenService.ComputeTokenHash("raw").Returns("hash");
        refreshTokenRepo.GetByTokenHashAsync("hash", Arg.Any<CancellationToken>()).Returns(token);

        var handler = new RefreshHandler(refreshTokenRepo, userRepo, tokenService, permissionRepo, auditWriter);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => handler.HandleAsync("raw", CancellationToken.None));

        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.TokenRefreshFailed, userId, "refresh", false, "token-revoked", Arg.Any<CancellationToken>());
        await auditWriter.Received(1).LogAuthAsync(
            AuditEventType.TokenRevoked, userId, "refresh-reuse-detected", false, "token-revoked", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterHandler_ValidRequest_LogsUserCreated()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        userRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = new RegisterHandler(userRepo, auditWriter);

        await handler.HandleAsync(new RegisterRequest("new@cortexa.io", "S3cur3P@ssw0rd!", "New User"), CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserCreated,
            "User",
            Arg.Any<string>(),
            "self-register",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrgUserHandler_ValidRequest_LogsUserCreated()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        userRepo.UsernameExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var orgId = Guid.NewGuid();

        var handler = new CreateOrgUserHandler(userRepo, auditWriter);

        await handler.HandleAsync(
            new CreateUserRequest("new@cortexa.io", "newuser", "P@ssw0rd!", Role.Researcher), orgId, CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserCreated,
            "User",
            Arg.Any<string>(),
            "create-org-user",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisableOrgUserHandler_ValidRequest_LogsUserDisabled()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: orgId);
        userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new DisableOrgUserHandler(userRepo, refreshTokenRepo, auditWriter);

        await handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserDisabled,
            "User",
            user.Id.ToString(),
            "disable-org-user",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableOrgUserHandler_ValidRequest_LogsUserEnabled()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: orgId, isEnabled: false);
        userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new EnableOrgUserHandler(userRepo, auditWriter);

        await handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserEnabled,
            "User",
            user.Id.ToString(),
            "enable-org-user",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeOrgUserRoleHandler_ValidRequest_LogsRoleChangeWithBeforeAfter()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: orgId);
        userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new ChangeOrgUserRoleHandler(userRepo, refreshTokenRepo, auditWriter);

        await handler.HandleAsync(user.Id, orgId, new ChangeRoleRequest(Role.Reviewer), CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserRoleChanged,
            "User",
            user.Id.ToString(),
            "change-org-user-role",
            Arg.Is<object>(o => o.ToString()!.Contains("Researcher") && o.ToString()!.Contains("Reviewer")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePasswordHandler_ValidRequest_LogsPasswordChangedWithoutPasswordValue()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();

        var currentHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass1", 4);
        var user = UserBuilder.Build(passwordHash: currentHash);
        userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new ChangePasswordHandler(userRepo, refreshTokenRepo, auditWriter);

        await handler.HandleAsync(
            user.Id, new ChangePasswordRequest("CurrentPass1", "NewP@ssword1"), CancellationToken.None);

        await auditWriter.Received(1).LogAsync(
            AuditEventType.UserPasswordChanged,
            "User",
            user.Id.ToString(),
            "change-password",
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProfileHandler_EmailChanged_LogsChangedFieldNameOnly()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(email: "old@example.com", username: "olduser", organizationId: orgId);
        userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        userRepo.EmailExistsInOrgExcludingUserAsync("new@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        userRepo.UsernameExistsExcludingUserAsync("olduser", user.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateProfileHandler(userRepo, auditWriter);

        object? capturedDetails = null;
        await auditWriter.LogAsync(
            Arg.Any<AuditEventType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<object?>(d => capturedDetails = d),
            Arg.Any<CancellationToken>());

        await handler.HandleAsync(
            user.Id, orgId, new UpdateProfileRequest("new@example.com", "olduser"), CancellationToken.None);

        var json = Cortexa.Identity.Application.Auditing.AuditDetailSerializer.Serialize(capturedDetails);
        Assert.NotNull(json);
        Assert.Contains("Email", json);
        Assert.DoesNotContain("new@example.com", json);
    }

    [Fact]
    public async Task UpdateRolePermissionsHandler_ValidRequest_LogsBeforeAfterPermissionLists()
    {
        var permRepo = Substitute.For<IPermissionRepository>();
        var refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        var userRepo = Substitute.For<IUserRepository>();
        var auditWriter = Substitute.For<IAuditWriter>();

        var readPerm = Permission.Create("patents.read", "read desc");
        var writePerm = Permission.Create("patents.write", "write desc");

        permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);
        permRepo.GetByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { readPerm });
        permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { readPerm, writePerm });

        var handler = new UpdateRolePermissionsHandler(permRepo, refreshTokenRepo, userRepo, auditWriter);

        object? capturedDetails = null;
        await auditWriter.LogAsync(
            Arg.Any<AuditEventType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<object?>(d => capturedDetails = d),
            Arg.Any<CancellationToken>());

        await handler.HandleAsync("Researcher", new UpdateRolePermissionsRequest(["patents.write"]), CancellationToken.None);

        var json = Cortexa.Identity.Application.Auditing.AuditDetailSerializer.Serialize(capturedDetails);
        Assert.NotNull(json);
        Assert.Contains("patents.read", json);
        Assert.Contains("patents.write", json);
    }
}
