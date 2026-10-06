using System.Net;
using Collector.Server.Infrastructure;
using Collector.Server.Infrastructure.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Collector.Server.Tests.Api;

public class HealthEndpointTests
{
    private const string LivePath = "/health/live";
    private const string ReadyPath = "/health/ready";
    private const string CosmosEndpointKey = "Cosmos:Endpoint";

    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        [CosmosEndpointKey] = "https://localhost:8081",
        ["Cosmos:Key"] = "dGVzdC1rZXk=",
        ["Messaging:RabbitMq:HostName"] = "localhost",
        ["Messaging:RabbitMq:Port"] = "5672",
        ["Messaging:RabbitMq:VirtualHost"] = "/",
        ["Messaging:RabbitMq:UserName"] = "collector",
        ["Messaging:RabbitMq:Password"] = "test-password"
    };

    [Fact]
    public async Task Live_ReturnsOkEvenWhenDependenciesAreUnhealthy()
    {
        await using var factory = CreateFactory(HealthStatus.Unhealthy, ValidSettings);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(LivePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsOkWhenChecksAreHealthy()
    {
        await using var factory = CreateFactory(HealthStatus.Healthy, ValidSettings);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ReadyPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailableWhenACheckFails()
    {
        await using var factory = CreateFactory(HealthStatus.Unhealthy, ValidSettings);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ReadyPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public void Startup_FailsWhenCosmosEndpointIsMissing()
    {
        var settings = new Dictionary<string, string?>(ValidSettings) { [CosmosEndpointKey] = string.Empty };
        using var factory = CreateFactory(HealthStatus.Healthy, settings);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(CosmosEndpointKey, Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_FailsWhenRabbitMqHostIsMissing()
    {
        var settings = new Dictionary<string, string?>(ValidSettings) { ["Messaging:RabbitMq:HostName"] = string.Empty };
        using var factory = CreateFactory(HealthStatus.Healthy, settings);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Messaging:RabbitMq:HostName", Flatten(exception), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(HealthStatus status, Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services => services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "fake",
                    _ => new FakeHealthCheck(status),
                    failureStatus: null,
                    tags: [HealthTags.Ready]));
            }));
        });

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class FakeHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(status));
    }
}
