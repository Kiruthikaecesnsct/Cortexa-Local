using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class EntraLoginHandlerTests
{
    private const string ValidEmail = "alice@cortexa.io";
    private const string ValidIdToken = "valid.entra.idtoken";
    private const string MockAccessToken = "mock.access.token";
    private const string MockRawRefreshToken = "raw-refresh-xyz789";
    private const string MockRefreshTokenHash = "hashed-refresh-xyz789";
    private const string EntraObjectId = "abc123-def456-ghi789";

    private static readonly Guid DefaultOrgId = new("00000000-0000-0000-0000-000000000001");

    private readonly IEntraTokenValidator _tokenValidator;
    private readonly IEntraGroupMapper _groupMapper;
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly EntraLoginHandler _handler;

    public EntraLoginHandlerTests()
    {
        _tokenValidator = Substitute.For<IEntraTokenValidator>();
        _groupMapper = Substitute.For<IEntraGroupMapper>();
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _tokenService = Substitute.For<ITokenService>();
        _permissionRepo = Substitute.For<IPermissionRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _permissionRepo.GetPermissionNamesForUserAsync(Arg.Any<Domain.Enums.Role>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _handler = new EntraLoginHandler(
            _tokenValidator,
            _groupMapper,
            _userRepo,
            _refreshTokenRepo,
            _tokenService,
            _permissionRepo,
            _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_NewEmail_CreatesUserWithMappedRoleAndReturnsTokens()
    {
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string> { "group-1" });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Reviewer, DefaultOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        Assert.Equal(MockRawRefreshToken, result.RawRefreshToken);
        await _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExistingEmail_DoesNotCreateSecondUserReturnsTokens()
    {
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string>());
        var existingUser = UserBuilder.Build(email: ValidEmail, entraObjectId: EntraObjectId);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Researcher, DefaultOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(existingUser);
        _tokenService.GenerateAccessToken(existingUser, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        Assert.Equal(MockRawRefreshToken, result.RawRefreshToken);
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_GroupsMappedToAdminRole_CreatesUserWithAdminRole()
    {
        var adminGroupId = "admin-group-123";
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string> { adminGroupId });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        User? capturedUser = null;

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Admin, DefaultOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(
            Arg.Do<User>(u => capturedUser = u),
            Arg.Any<CancellationToken>());
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.Equal(Role.Admin, capturedUser!.Role);
    }

    [Fact]
    public async Task HandleAsync_InvalidToken_PropagatesUnauthorizedException()
    {
        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns<EntraClaims>(_ => throw new UnauthorizedException());

        var request = new EntraLoginRequest(ValidIdToken);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RefreshTokenPersistedAsHashNotRaw()
    {
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string>());
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        RefreshToken? capturedToken = null;

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Researcher, DefaultOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        await _refreshTokenRepo.AddAsync(
            Arg.Do<RefreshToken>(t => capturedToken = t),
            Arg.Any<CancellationToken>());

        var request = new EntraLoginRequest(ValidIdToken);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedToken);
        Assert.Equal(MockRefreshTokenHash, capturedToken!.TokenHash);
        Assert.NotEqual(MockRawRefreshToken, capturedToken.TokenHash);
    }

    [Fact]
    public async Task HandleAsync_TwoUsersWithSameEmailLocalPartButDifferentOids_ProduceUniqueUsernames()
    {
        var email1 = "alice@cortexa.io";
        var email2 = "alice@external.com";
        var oid1 = "aaaaaaaa-bbbb-cccc-dddd-111111111111";
        var oid2 = "aaaaaaaa-bbbb-cccc-dddd-222222222222";
        var claims1 = new EntraClaims(email1, "Alice One", oid1, new List<string>());
        var claims2 = new EntraClaims(email2, "Alice Two", oid2, new List<string>());
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        User? capturedUser1 = null;
        User? capturedUser2 = null;
        var callCount = 0;

        _tokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(claims1, claims2);
        _groupMapper.Map(Arg.Any<IReadOnlyList<string>>())
            .Returns(new EntraAssignment(Role.Researcher, DefaultOrgId));
        _userRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (callCount == 0)
                    capturedUser1 = callInfo.Arg<User>();
                else
                    capturedUser2 = callInfo.Arg<User>();
                callCount++;
                return Task.CompletedTask;
            });
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(new EntraLoginRequest(ValidIdToken), CancellationToken.None);
        await _handler.HandleAsync(new EntraLoginRequest(ValidIdToken), CancellationToken.None);

        Assert.NotNull(capturedUser1);
        Assert.NotNull(capturedUser2);
        Assert.NotEqual(capturedUser1!.Username, capturedUser2!.Username);
        Assert.StartsWith("alice_", capturedUser1.Username);
        Assert.StartsWith("alice_", capturedUser2.Username);
        Assert.Contains(oid1, capturedUser1.Username);
        Assert.Contains(oid2, capturedUser2.Username);
    }

    [Fact]
    public async Task HandleAsync_LongEmailLocalPart_ProducesUsernameFittingWithin100CharsPreservingFullOid()
    {
        var longLocalPart = new string('a', 64);
        var email = $"{longLocalPart}@example.com";
        var oid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";
        var claims = new EntraClaims(email, "Long Name", oid, new List<string>());
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        User? capturedUser = null;

        _tokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(Arg.Any<IReadOnlyList<string>>())
            .Returns(new EntraAssignment(Role.Researcher, DefaultOrgId));
        _userRepo.GetByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(
            Arg.Do<User>(u => capturedUser = u),
            Arg.Any<CancellationToken>());
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.True(capturedUser!.Username.Length <= 100);
        Assert.EndsWith($"_{oid}", capturedUser.Username);
    }

    [Fact]
    public async Task HandleAsync_ExistingUserWithChangedGroupMembership_ReOrgsUserViaApplyEntraMapping()
    {
        var existingUser = UserBuilder.Build(
            email: ValidEmail, entraObjectId: EntraObjectId, role: Role.Researcher, organizationId: DefaultOrgId);
        var trackedUser = UserBuilder.Build(
            email: ValidEmail, entraObjectId: EntraObjectId, role: Role.Researcher, organizationId: DefaultOrgId);
        var newOrgId = Guid.NewGuid();
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string> { "admin-group" });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        User? capturedUser = null;

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Admin, newOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(existingUser);
        _userRepo.GetTrackedByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(trackedUser);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(callInfo =>
            {
                capturedUser = callInfo.Arg<User>();
                return (MockAccessToken, expiresAt);
            });
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.Equal(Role.Admin, capturedUser!.Role);
        Assert.Equal(newOrgId, capturedUser.OrganizationId);
        await _userRepo.Received(1).GetTrackedByEmailAsync(ValidEmail, Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExistingUserNoMappingChange_DoesNotFetchTrackedOrSaveChanges()
    {
        var existingUser = UserBuilder.Build(
            email: ValidEmail, entraObjectId: EntraObjectId, role: Role.Reviewer, organizationId: DefaultOrgId);
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string> { "reviewer-group" });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Reviewer, DefaultOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(existingUser);
        _tokenService.GenerateAccessToken(existingUser, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        await _userRepo.DidNotReceive().GetTrackedByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExistingSystemUser_NeverMutatedEvenWhenMappingImpliesChange()
    {
        var systemUser = UserBuilder.Build(
            email: ValidEmail,
            entraObjectId: EntraObjectId,
            role: Role.Admin,
            organizationId: DefaultOrgId,
            isSystem: true);
        var differentOrgId = Guid.NewGuid();
        var claims = new EntraClaims(ValidEmail, "System Account", EntraObjectId, new List<string> { "superadmin-group" });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        User? capturedUser = null;

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Admin, differentOrgId));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(systemUser);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(callInfo =>
            {
                capturedUser = callInfo.Arg<User>();
                return (MockAccessToken, expiresAt);
            });
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.Equal(Role.Admin, capturedUser!.Role);
        Assert.Equal(DefaultOrgId, capturedUser.OrganizationId);
        await _userRepo.DidNotReceive().GetTrackedByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TrackedUserRaceReturnsNull_ReturnsOriginalUserWithoutSaving()
    {
        var existingUser = UserBuilder.Build(
            email: ValidEmail, entraObjectId: EntraObjectId, role: Role.Researcher, organizationId: DefaultOrgId);
        var claims = new EntraClaims(ValidEmail, "Alice Smith", EntraObjectId, new List<string> { "admin-group" });
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenValidator.ValidateAsync(ValidIdToken, Arg.Any<CancellationToken>())
            .Returns(claims);
        _groupMapper.Map(claims.GroupIds)
            .Returns(new EntraAssignment(Role.Admin, Guid.NewGuid()));
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(existingUser);
        _userRepo.GetTrackedByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _tokenService.GenerateAccessToken(existingUser, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new EntraLoginRequest(ValidIdToken);

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        await _userRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
