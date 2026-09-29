using System.Security.Claims;
using Cortexa.JobOrchestrator.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class CallerContextResolverTests
{
    private static HttpContext BuildContext(
        string? orgId,
        string? userId = null,
        string? role = null)
    {
        var ctx = new DefaultHttpContext();

        if (orgId is not null)
            ctx.Request.Headers["X-Org-Id"] = orgId;

        if (userId is not null)
            ctx.Request.Headers["X-User-Id"] = userId;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-fallback") };
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));

        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));
        return ctx;
    }

    [Fact]
    public void Resolve_MissingOrgIdHeader_NonSuperAdmin_ReturnsFailure()
    {
        var ctx = BuildContext(orgId: null, role: "Admin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Missing or blank X-Org-Id header.");
    }

    [Fact]
    public void Resolve_BlankOrgIdHeader_NonSuperAdmin_ReturnsFailure()
    {
        var ctx = BuildContext(orgId: "   ", role: "Admin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Missing or blank X-Org-Id header.");
    }

    [Fact]
    public void Resolve_MissingOrgIdHeader_SuperAdmin_ReturnsSuccessWithNullOrgId()
    {
        var ctx = BuildContext(orgId: null, userId: "super-user-1", role: "SuperAdmin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.OrgId.Should().BeNull();
        result.Context.UserId.Should().Be("super-user-1");
        result.Context.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Resolve_BlankOrgIdHeader_SuperAdmin_ReturnsSuccessWithNullOrgId()
    {
        var ctx = BuildContext(orgId: "   ", userId: "super-user-2", role: "SuperAdmin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.OrgId.Should().BeNull();
        result.Context.UserId.Should().Be("super-user-2");
        result.Context.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Resolve_PresentOrgIdHeader_SuperAdmin_PreservesOrgId()
    {
        var ctx = BuildContext(orgId: "org-100", userId: "super-user-3", role: "SuperAdmin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.OrgId.Should().Be("org-100");
        result.Context.UserId.Should().Be("super-user-3");
        result.Context.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Resolve_OrgIdHeaderPresent_ReturnsSuccessWithOrgId()
    {
        var ctx = BuildContext(orgId: "org-42", userId: "user-9");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.OrgId.Should().Be("org-42");
        result.Context.UserId.Should().Be("user-9");
        result.Context.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SuperAdminRoleClaim_SetsIsSuperAdminTrue()
    {
        var ctx = BuildContext(orgId: "org-42", role: "SuperAdmin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void Resolve_AdminRoleClaim_SetsIsSuperAdminFalse()
    {
        var ctx = BuildContext(orgId: "org-42", role: "Admin");

        var result = CallerContextResolver.Resolve(ctx);

        result.IsSuccess.Should().BeTrue();
        result.Context!.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void Resolve_MissingUserIdHeader_FallsBackToNameIdentifierClaim()
    {
        var ctx = BuildContext(orgId: "org-42");

        var result = CallerContextResolver.Resolve(ctx);

        result.Context!.UserId.Should().Be("user-fallback");
    }
}
