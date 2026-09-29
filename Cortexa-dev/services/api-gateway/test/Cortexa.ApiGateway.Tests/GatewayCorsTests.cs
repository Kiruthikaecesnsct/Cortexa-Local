using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class GatewayCorsTests
{
    private const string AllowedOrigin = "http://localhost:5173";
    private const string UnlistedOrigin = "http://evil.example.com";
    private const string TestSigningKey = "dev-symmetric-key-min-256-bits-long-shared-with-identity-service-for-local-development-only";

    [Fact]
    public async Task Preflight_FromAllowedOrigin_Returns204WithCorsHeader()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Cors:AllowedOrigins:0"] = AllowedOrigin
                    });
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(HttpMethod.Options, "/ingestion/test");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.True(
            (int)response.StatusCode is >= 200 and < 300,
            $"Expected 2xx but got {(int)response.StatusCode}");
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Response missing Access-Control-Allow-Origin header");
    }

    [Fact]
    public async Task Preflight_WhenNoOriginsConfigured_DeniesAllOrigins()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddCors(options =>
                    {
                        options.AddPolicy(
                            Cortexa.ApiGateway.Api.Cors.CorsServiceCollectionExtensions.PolicyName,
                            policy => policy.WithOrigins(Array.Empty<string>()));
                    });
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(HttpMethod.Options, "/ingestion/test");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Response must not contain Access-Control-Allow-Origin when no origins are configured");
    }

    [Fact]
    public async Task Preflight_FromUnlistedOrigin_DeniesOrigin()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Cors:AllowedOrigins:0"] = AllowedOrigin
                    });
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(HttpMethod.Options, "/ingestion/test");
        request.Headers.Add("Origin", UnlistedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Response must not contain Access-Control-Allow-Origin for an unlisted origin");
    }
}
