using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

internal sealed class HeaderCaptureServer : IDisposable
{
    private readonly HttpListener _listener;

    public string Address { get; }

    public Task<System.Collections.Specialized.NameValueCollection> CapturedHeaders { get; }

    private HeaderCaptureServer(HttpListener listener, string address, Task<System.Collections.Specialized.NameValueCollection> capturedHeaders)
    {
        _listener = listener;
        Address = address;
        CapturedHeaders = capturedHeaders;
    }

    public static Task<HeaderCaptureServer> StartAsync()
    {
        var port = GetFreeTcpPort();
        var address = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();

        var tcs = new TaskCompletionSource<System.Collections.Specialized.NameValueCollection>();
        _ = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            tcs.SetResult(context.Request.Headers);
            context.Response.StatusCode = 200;
            context.Response.Close();
        });

        return Task.FromResult(new HeaderCaptureServer(listener, address, tcs.Task));
    }

    private static int GetFreeTcpPort()
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Close();
    }
}

public sealed class GatewayAuthTests : IClassFixture<GatewayTestFactory>
{
    private readonly GatewayTestFactory _factory;

    public GatewayAuthTests(GatewayTestFactory factory)
    {
        _factory = factory;
        _factory.UserStatusClient.Enabled = true;
        _factory.UserStatusClient.ShouldFail = false;
        _factory.UserStatusClient.SecurityStamp = TestJwtFactory.DefaultSecurityStamp;
    }

    [Fact]
    public async Task ValidToken_ReturnsNotFound_WhenDestinationUnreachable()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateExpiredToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateTamperedToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/ingestion/test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthRoute_AllowsAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/test");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_AllowsAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationId_AddedWhenMissing()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var correlationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task CorrelationId_PreservedWhenProvided()
    {
        var client = _factory.CreateClient();
        var providedId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", providedId);

        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var returnedId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.Equal(providedId, returnedId);
    }

    [Fact]
    public async Task InvalidCorrelationId_ReplacedWithNewGuid()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "not-a-guid");

        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var correlationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.NotEqual("not-a-guid", correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task AuthenticatedUser_WrongRole_ReturnsForbidden()
    {
        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Routes:admin:ClusterId"] = "ingestion",
                        ["ReverseProxy:Routes:admin:Match:Path"] = "/admin/{**catch-all}",
                        ["ReverseProxy:Routes:admin:Metadata:RequiredRole"] = "Admin"
                    });
                });
            });

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken("Researcher");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/admin/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/ingestion/health")]
    [InlineData("/extraction/health")]
    [InlineData("/evidence/health")]
    [InlineData("/scoring/health")]
    [InlineData("/harvesting/health")]
    [InlineData("/seeding/health")]
    [InlineData("/orchestrator/health")]
    [InlineData("/model/health")]
    [InlineData("/vector/health")]
    [InlineData("/auth/health")]
    public async Task BackendHealthRoute_AllowsAnonymous(string healthPath)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(healthPath);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionGatedRoute_Returns403_WhenPermissionMissing()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/vector/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermissionGatedRoute_Succeeds_WhenPermissionPresent()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/vector/test");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_BypassesRoleAndPermissionChecks()
    {
        var client = _factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken("SuperAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/vector/test");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RouteWithoutRbacMetadata_DeniedByDefault()
    {
        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ReverseProxy:Routes:nometadata:ClusterId"] = "ingestion",
                        ["ReverseProxy:Routes:nometadata:Match:Path"] = "/nometadata/{**catch-all}"
                    });
                });
            });

        var client = factory.CreateClient();
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:write" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/nometadata/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IdentityHeaders_InjectedFromClaims_ReachDownstream()
    {
        using var capture = await HeaderCaptureServer.StartAsync();
        var orgId = Guid.NewGuid();

        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
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
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:read" }, orgId: orgId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.GetAsync("/vector/test");

        var headers = await capture.CapturedHeaders;
        Assert.Equal("test-user-id", headers["X-User-Id"]);
        Assert.Equal("test@cortexa.local", headers["X-User-Email"]);
        Assert.Equal(orgId.ToString(), headers["X-Org-Id"]);
    }

    [Fact]
    public async Task SpoofedOrgIdHeader_StrippedNotPassedThrough()
    {
        using var capture = await HeaderCaptureServer.StartAsync();

        var factory = new GatewayTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
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
        var token = TestJwtFactory.CreateValidToken(perms: new[] { "documents:read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Org-Id", "spoofed-org-id");

        await client.GetAsync("/vector/test");

        var headers = await capture.CapturedHeaders;
        Assert.Null(headers["X-Org-Id"]);
    }

    [Fact]
    public void HealthRoutes_RewritePathToBackendRootHealth()
    {
        // Backends serve /health at the root, so each /<svc>/health route must rewrite
        // the forwarded path to /health (PathSet) — otherwise the backend 404s.
        var configProvider = _factory.Services
            .GetRequiredService<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>();
        var routes = configProvider.GetConfig().Routes;

        var healthRoutes = routes
            .Where(r => r.RouteId.EndsWith("-health", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(healthRoutes);
        foreach (var route in healthRoutes)
        {
            Assert.NotNull(route.Transforms);
            Assert.Contains(
                route.Transforms!,
                t => t.TryGetValue("PathSet", out var value) && value == "/health");
        }
    }

    [Theory]
    [InlineData("model", "/model")]
    [InlineData("vector", "/vector")]
    public void PrefixedCatchAllRoutes_StripServicePrefixBeforeForwarding(string routeId, string prefix)
    {
        // model-router and vector-router mount their routes at the root (e.g. /models,
        // /complete, /search). The gateway strips /api via UsePathBase, so each catch-all
        // route must also strip its own /<service> segment (PathRemovePrefix) or the
        // backend 404s — the BUG122 failure mode for GET /api/model/models.
        var configProvider = _factory.Services
            .GetRequiredService<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>();

        var route = configProvider.GetConfig().Routes
            .SingleOrDefault(r => r.RouteId == routeId);

        Assert.NotNull(route);
        Assert.NotNull(route!.Transforms);
        Assert.Contains(
            route.Transforms!,
            t => t.TryGetValue("PathRemovePrefix", out var value) && value == prefix);
    }
}
