using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class RbacMatrixTests : IClassFixture<GatewayTestFactory>
{
    private static readonly string[] ResearcherPerms =
    [
        "documents:read", "documents:write", "jobs:submit", "jobs:read", "reports:read"
    ];

    private static readonly string[] ReviewerPerms =
    [
        "documents:read", "jobs:read", "reports:read", "reports:export"
    ];

    private static readonly string[] AdminPerms =
    [
        "documents:read", "documents:write", "jobs:submit", "jobs:read",
        "reports:read", "reports:export", "admin:users:read", "admin:users:write"
    ];

    private static readonly string[] AdminConfigPerms =
    [
        "documents:read", "documents:write", "jobs:submit", "jobs:read",
        "reports:read", "reports:export", "admin:users:read", "admin:users:write",
        "admin:config:read", "admin:config:write"
    ];

    private readonly GatewayTestFactory _factory;

    public RbacMatrixTests(GatewayTestFactory factory)
    {
        _factory = factory;
        _factory.UserStatusClient.Enabled = true;
        _factory.UserStatusClient.ShouldFail = false;
        _factory.UserStatusClient.SecurityStamp = TestJwtFactory.DefaultSecurityStamp;
    }

    [Theory]
    [InlineData("/ingestion/test")]
    [InlineData("/extraction/test")]
    [InlineData("/evidence/test")]
    [InlineData("/scoring/test")]
    [InlineData("/harvesting/test")]
    [InlineData("/seeding/test")]
    [InlineData("/orchestrator/test")]
    [InlineData("/batches/test")]
    [InlineData("/documents/test")]
    [InlineData("/model/test")]
    [InlineData("/vector/test")]
    public async Task ResearcherRole_CanAccess_AllowedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Researcher", perms: ResearcherPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/test")]
    public async Task ResearcherRole_CannotAccess_DeniedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Researcher", perms: ResearcherPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/extraction/test")]
    [InlineData("/evidence/test")]
    [InlineData("/scoring/test")]
    [InlineData("/harvesting/test")]
    [InlineData("/seeding/test")]
    [InlineData("/orchestrator/test")]
    [InlineData("/documents/test")]
    [InlineData("/model/test")]
    [InlineData("/vector/test")]
    public async Task ReviewerRole_CanAccess_AllowedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Reviewer", perms: ReviewerPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/ingestion/test")]
    [InlineData("/batches/test")]
    [InlineData("/admin/test")]
    public async Task ReviewerRole_CannotAccess_DeniedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Reviewer", perms: ReviewerPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/ingestion/test")]
    [InlineData("/extraction/test")]
    [InlineData("/evidence/test")]
    [InlineData("/scoring/test")]
    [InlineData("/harvesting/test")]
    [InlineData("/seeding/test")]
    [InlineData("/orchestrator/test")]
    [InlineData("/batches/test")]
    [InlineData("/documents/test")]
    [InlineData("/model/test")]
    [InlineData("/vector/test")]
    [InlineData("/admin/test")]
    public async Task AdminRole_CanAccess_AllRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Admin", perms: AdminPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/ingestion/test")]
    [InlineData("/extraction/test")]
    [InlineData("/evidence/test")]
    [InlineData("/scoring/test")]
    [InlineData("/harvesting/test")]
    [InlineData("/seeding/test")]
    [InlineData("/orchestrator/test")]
    [InlineData("/batches/test")]
    [InlineData("/documents/test")]
    [InlineData("/model/test")]
    [InlineData("/vector/test")]
    [InlineData("/admin/test")]
    public async Task SuperAdminRole_CanAccess_AllProtectedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "SuperAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/ingestion/test")]
    [InlineData("/extraction/test")]
    [InlineData("/evidence/test")]
    [InlineData("/scoring/test")]
    [InlineData("/harvesting/test")]
    [InlineData("/seeding/test")]
    [InlineData("/orchestrator/test")]
    [InlineData("/batches/test")]
    [InlineData("/documents/test")]
    [InlineData("/model/test")]
    [InlineData("/vector/test")]
    [InlineData("/admin/test")]
    public async Task AuthenticatedUserWithNoPerms_IsDenied_OnPermissionGatedRoute(string path)
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminRole_CanPut_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "SuperAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync("/api/config", JsonContent());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRole_CanPut_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Admin", perms: AdminConfigPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync("/api/config", JsonContent());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ResearcherRole_IsDenied_OnPut_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Researcher", perms: ResearcherPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync("/api/config", JsonContent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewerRole_IsDenied_OnPut_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Reviewer", perms: ReviewerPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync("/api/config", JsonContent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Admin")]
    public async Task AdminOrSuperAdminRole_CanGet_ApiConfig(string role)
    {
        var client = _factory.CreateClient();
        var perms = role == "Admin" ? AdminConfigPerms : null;
        var token = TestJwtFactory.CreateValidToken(role: role, perms: perms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/config");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ResearcherRole_CanGet_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Researcher", perms: ResearcherPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/config");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewerRole_CanGet_ApiConfig()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(role: "Reviewer", perms: ReviewerPerms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/config");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedUser_IsDenied_OnGet_ApiConfig()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static StringContent JsonContent()
        => new("{}", System.Text.Encoding.UTF8, "application/json");
}
