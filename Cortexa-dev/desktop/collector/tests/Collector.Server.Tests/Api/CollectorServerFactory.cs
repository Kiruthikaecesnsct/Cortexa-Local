using System.Net.Http.Headers;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure.Health;
using Collector.Server.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Tests.Api;

internal sealed class CollectorServerFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = TestSettings.Create(overrides);

    public FakePipelineRowStore Store { get; } = new();

    public FakeIngestionEventPublisher Publisher { get; } = new();

    public FakeUserStatusReader Users { get; } = new();

    public FakeModelConfigReader Models { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public async Task<HttpResponseMessage> PostAsync(UploadCall call)
    {
        using var client = CreateClient();
        using var request = call.ToRequest();
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string? token = null)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            Replace<IPipelineRowStore>(services, Store);
            Replace<IIngestionEventPublisher>(services, Publisher);
            Replace<IUserStatusReader>(services, Users);
            Replace<IModelConfigReader>(services, Models);
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "fake",
                    _ => new HealthyCheck(),
                    failureStatus: null,
                    tags: [HealthTags.Ready]));
            });
        });
    }

    private static void Replace<TService>(IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
    }

    private sealed class HealthyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }
}
