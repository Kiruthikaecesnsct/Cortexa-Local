using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class UserStatusMiddlewareTests
{
    [Fact]
    public async Task DisabledUser_ValidUnexpiredToken_ReturnsForbidden()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.Enabled = false;

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaleStamp_AfterRoleChange_ReturnsForbidden()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.Enabled = true;
        factory.UserStatusClient.SecurityStamp = Guid.NewGuid();

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(
            perms: new[] { "documents:write" },
            stamp: TestJwtFactory.DefaultSecurityStamp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EnabledUser_MatchingStamp_IsNotRejectedByStatusCheck()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.Enabled = true;
        factory.UserStatusClient.SecurityStamp = TestJwtFactory.DefaultSecurityStamp;

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IdentityLookupFails_NoCachedEntry_FailsClosedWithServiceUnavailable()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.ShouldFail = true;

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task IdentityLookupFails_WithCachedEntry_UsesStaleCacheInsteadOfFailing()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.Enabled = true;
        factory.UserStatusClient.SecurityStamp = TestJwtFactory.DefaultSecurityStamp;

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var firstResponse = await client.GetAsync("/ingestion/test");
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, firstResponse.StatusCode);

        factory.UserStatusClient.ShouldFail = true;

        var secondResponse = await client.GetAsync("/ingestion/test");

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, secondResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, secondResponse.StatusCode);
    }

    [Fact]
    public async Task AnonymousRoute_NotAffectedByUserStatusCheck()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.ShouldFail = true;

        var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/test");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.UserStatusClient.CallCount);
    }

    [Fact]
    public async Task HealthEndpoint_NotAffectedByUserStatusCheck()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.ShouldFail = true;

        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.UserStatusClient.CallCount);
    }

    [Fact]
    public async Task MissingStampClaim_ReturnsForbidden()
    {
        using var factory = new GatewayTestFactory();
        factory.UserStatusClient.Enabled = true;

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidTokenWithoutStampClaim();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void MissingIdentityInternalBaseUrl_FailsStartupValidation()
    {
        using var factory = new GatewayTestFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("UserStatus:IdentityInternalBaseUrl", string.Empty));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("UserStatus:IdentityInternalBaseUrl is required", exception.ToString());
    }
}
