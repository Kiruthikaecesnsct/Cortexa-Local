using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class RegisterHandlerTests
{
    private const string ValidEmail = "newuser@cortexa.io";
    private const string ValidPassword = "S3cur3P@ssw0rd!";
    private const string ValidDisplayName = "New User";
    private const string ExistingEmail = "existing@cortexa.io";

    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly RegisterHandler _handler;

    public RegisterHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new RegisterHandler(_userRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesUserWithResearcherRole()
    {
        User? capturedUser = null;

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(
            Arg.Do<User>(u => capturedUser = u),
            Arg.Any<CancellationToken>());

        var request = new RegisterRequest(ValidEmail, ValidPassword, ValidDisplayName);

        var response = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.Equal(ValidEmail, capturedUser!.Email);
        Assert.Equal(ValidDisplayName, capturedUser.Username);
        Assert.Equal(Role.Researcher, capturedUser.Role);
        Assert.NotNull(capturedUser.PasswordHash);
        Assert.NotEqual(ValidPassword, capturedUser.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_ReturnsUserDetails()
    {
        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());

        var request = new RegisterRequest(ValidEmail, ValidPassword, ValidDisplayName);

        var response = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(ValidEmail, response.Email);
        Assert.Equal(ValidDisplayName, response.DisplayName);
        Assert.NotEqual(Guid.Empty, response.UserId);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_HashesPassword()
    {
        User? capturedUser = null;

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(
            Arg.Do<User>(u => capturedUser = u),
            Arg.Any<CancellationToken>());

        var request = new RegisterRequest(ValidEmail, ValidPassword, ValidDisplayName);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.True(BCrypt.Net.BCrypt.Verify(ValidPassword, capturedUser!.PasswordHash));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_AssignsDefaultOrganization()
    {
        User? capturedUser = null;

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        await _userRepo.AddAsync(
            Arg.Do<User>(u => capturedUser = u),
            Arg.Any<CancellationToken>());

        var request = new RegisterRequest(ValidEmail, ValidPassword, ValidDisplayName);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedUser);
        Assert.Equal(OrganizationConstants.DefaultOrganizationId, capturedUser!.OrganizationId);
    }

    [Fact]
    public async Task HandleAsync_DuplicateEmail_ThrowsDuplicateEmailException()
    {
        var existingUser = UserBuilder.Build(email: ExistingEmail);

        _userRepo.GetByEmailAsync(ExistingEmail, Arg.Any<CancellationToken>())
            .Returns(existingUser);

        var request = new RegisterRequest(ExistingEmail, ValidPassword, ValidDisplayName);

        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_NullOrWhitespaceDisplayName_ThrowsBadRequestException(string? displayName)
    {
        var request = new RegisterRequest(ValidEmail, ValidPassword, displayName!);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.Contains("Username must not be empty", ex.Message);
        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DisplayNameTooShort_ThrowsBadRequestException()
    {
        var request = new RegisterRequest(ValidEmail, ValidPassword, "A");

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.Contains("Username must be between 2 and 100 characters", ex.Message);
        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DisplayNameTooLong_ThrowsBadRequestException()
    {
        var longName = new string('A', 101);
        var request = new RegisterRequest(ValidEmail, ValidPassword, longName);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.Contains("Username must be between 2 and 100 characters", ex.Message);
        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public async Task HandleAsync_InvalidEmail_ThrowsBadRequestException(string invalidEmail)
    {
        var request = new RegisterRequest(invalidEmail, ValidPassword, ValidDisplayName);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.Contains("A valid email address is required", ex.Message);
        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task HandleAsync_WeakPassword_ThrowsBadRequestException(string weakPassword)
    {
        var request = new RegisterRequest(ValidEmail, weakPassword, ValidDisplayName);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.Contains("Password must be at least 8 characters", ex.Message);
        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidInput_DoesNotCallRepositoryOnValidationFailure()
    {
        var request = new RegisterRequest(ValidEmail, "short", ValidDisplayName);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _userRepo.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }
}
