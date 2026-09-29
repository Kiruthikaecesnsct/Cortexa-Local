using System.IdentityModel.Tokens.Jwt;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Services;

public sealed class JwtTokenServiceTests
{
    private const string SigningKey = "this-is-a-test-signing-key-with-enough-length-1234567890";
    private const string Issuer = "cortexa-identity-tests";
    private const string Audience = "cortexa-tests";

    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        var settings = Options.Create(new JwtSettings
        {
            SigningKey = SigningKey,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenExpiryMinutes = 30
        });

        _service = new JwtTokenService(settings);
    }

    [Fact]
    public void GenerateAccessToken_WhenUserHasOrganization_IncludesOrgIdClaim()
    {
        var organizationId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: organizationId);

        var (token, _) = _service.GenerateAccessToken(user, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var orgClaim = jwt.Claims.SingleOrDefault(c => c.Type == "org_id");

        Assert.NotNull(orgClaim);
        Assert.Equal(organizationId.ToString(), orgClaim!.Value);
    }

    [Fact]
    public void GenerateAccessToken_WhenUserHasNoOrganization_OmitsOrgIdClaim()
    {
        var user = UserBuilder.Build(role: Role.SuperAdmin, isSystem: true, organizationId: null);

        var (token, _) = _service.GenerateAccessToken(user, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == "org_id");
    }

    [Fact]
    public void GenerateAccessToken_IncludesStampClaimMatchingUserSecurityStamp()
    {
        var user = UserBuilder.Build(role: Role.Researcher);

        var (token, _) = _service.GenerateAccessToken(user, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var stampClaim = jwt.Claims.SingleOrDefault(c => c.Type == "stamp");

        Assert.NotNull(stampClaim);
        Assert.Equal(user.SecurityStamp.ToString(), stampClaim!.Value);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public void GenerateAccessToken_StampsRoleAndRolesClaimsWithCorrectEnumName(Role role)
    {
        var user = UserBuilder.Build(role: role, isSystem: role == Role.SuperAdmin);

        var (token, _) = _service.GenerateAccessToken(user, Array.Empty<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roleClaim = jwt.Claims.SingleOrDefault(c => c.Type == "role");
        var rolesClaimValues = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToArray();

        Assert.NotNull(roleClaim);
        Assert.Equal(role.ToString(), roleClaim!.Value);

        Assert.Equal(new[] { role.ToString() }, rolesClaimValues);
    }
}
