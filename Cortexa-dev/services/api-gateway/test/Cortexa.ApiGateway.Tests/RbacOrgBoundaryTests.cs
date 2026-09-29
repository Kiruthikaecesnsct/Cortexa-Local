using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class RbacOrgBoundaryTests : IClassFixture<GatewayTestFactory>
{
    private static readonly string[] AdminPerms =
    [
        "documents:read", "documents:write", "jobs:submit", "jobs:read",
        "reports:read", "reports:export", "admin:users:read", "admin:users:write"
    ];

    private readonly GatewayTestFactory _factory;

    public RbacOrgBoundaryTests(GatewayTestFactory factory)
    {
        _factory = factory;
        _factory.UserStatusClient.Enabled = true;
        _factory.UserStatusClient.ShouldFail = false;
        _factory.UserStatusClient.SecurityStamp = TestJwtFactory.DefaultSecurityStamp;
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToAdminRoute_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResearcherWithoutAdminPerm_AccessingAdminRoute_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(
            role: "Researcher",
            perms: ["documents:read", "documents:write", "jobs:submit", "jobs:read", "reports:read"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminWithAdminPerm_AccessingAdminRoute_Passes()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Admin", perms: AdminPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/admin/users");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SpoofedUserIdHeader_IsStrippedAndReplacedFromSubClaim()
    {
        using var capture = await HeaderCaptureServer.StartAsync();

        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Clusters:vector-router:Destinations:d1:Address"] = capture.Address,
                        ["ReverseProxy:Clusters:vector-router:HealthCheck:Active:Enabled"] = "false",
                        ["ReverseProxy:Clusters:vector-router:HealthCheck:Passive:Enabled"] = "false"
                    });
                });
            });

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: ["documents:read"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-User-Id", "spoofed-user-id");

        await client.GetAsync("/vector/test");

        var headers = await capture.CapturedHeaders;
        Assert.NotEqual("spoofed-user-id", headers["X-User-Id"]);
        Assert.Equal("test-user-id", headers["X-User-Id"]);
    }

    [Fact]
    public async Task SpoofedUserEmailHeader_IsStrippedAndReplacedFromEmailClaim()
    {
        using var capture = await HeaderCaptureServer.StartAsync();

        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Clusters:vector-router:Destinations:d1:Address"] = capture.Address,
                        ["ReverseProxy:Clusters:vector-router:HealthCheck:Active:Enabled"] = "false",
                        ["ReverseProxy:Clusters:vector-router:HealthCheck:Passive:Enabled"] = "false"
                    });
                });
            });

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: ["documents:read"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-User-Email", "spoofed@hacker.com");

        await client.GetAsync("/vector/test");

        var headers = await capture.CapturedHeaders;
        Assert.NotEqual("spoofed@hacker.com", headers["X-User-Email"]);
        Assert.Equal("test@cortexa.local", headers["X-User-Email"]);
    }
}
