using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace Cortexa.ModelRouter.Tests;

public sealed class FoundryTimeoutTests
{
    [Fact]
    public async Task CompleteAsync_ServerSideBudgetExceeded_ThrowsFoundryTimeoutException()
    {
        var settings = new FoundrySettings
        {
            Endpoint = "https://test.openai.azure.com",
            Deployment = "gpt-5.5",
            ApiVersion = "preview",
            ApiKeySecretName = "test-key",
            CallTimeoutSeconds = 1,
            AcquireWaitSeconds = 2,
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                ["gpt-5.5"] = new() { MaxInFlight = 10 }
            }
        };

        var httpClient = new HttpClient(new SlowResponseHandler(TimeSpan.FromSeconds(10)));
        var keyResolver = new FakeKeyResolver("test-api-key");
        var limiter = new FoundryConcurrencyLimiter();
        var provider = new FoundryProvider(
            Options.Create(settings),
            httpClient,
            keyResolver,
            limiter,
            NullLogger<FoundryProvider>.Instance);

        var request = new ModelRequest(
            ModelMode.SinglePrimary,
            "patent_research",
            "test prompt",
            null,
            new ModelOptions(1000, 1.0f),
            "gpt-5.5");

        var act = async () => await provider.CompleteAsync(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<FoundryTimeoutException>();
        exception.Which.Deployment.Should().Be("gpt-5.5");
        exception.Which.TimeoutSeconds.Should().Be(1);
    }

    [Fact]
    public async Task StreamAsync_ServerSideBudgetExceeded_ThrowsFoundryTimeoutException()
    {
        var settings = new FoundrySettings
        {
            Endpoint = "https://test.openai.azure.com",
            Deployment = "gpt-5.5",
            ApiVersion = "preview",
            ApiKeySecretName = "test-key",
            CallTimeoutSeconds = 1,
            AcquireWaitSeconds = 2,
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                ["gpt-5.5"] = new() { MaxInFlight = 10 }
            }
        };

        var httpClient = new HttpClient(new SlowResponseHandler(TimeSpan.FromSeconds(10)));
        var keyResolver = new FakeKeyResolver("test-api-key");
        var limiter = new FoundryConcurrencyLimiter();
        var provider = new FoundryProvider(
            Options.Create(settings),
            httpClient,
            keyResolver,
            limiter,
            NullLogger<FoundryProvider>.Instance);

        var request = new ModelRequest(
            ModelMode.SinglePrimary,
            "patent_research",
            "test prompt",
            null,
            new ModelOptions(1000, 1.0f),
            "gpt-5.5");

        var act = async () =>
        {
            await foreach (var chunk in provider.StreamAsync(request, CancellationToken.None))
            {
            }
        };

        var exception = await act.Should().ThrowAsync<FoundryTimeoutException>();
        exception.Which.Deployment.Should().Be("gpt-5.5");
        exception.Which.TimeoutSeconds.Should().Be(1);
    }

    [Fact]
    public async Task AcquireAsync_ConfigurableWaitTimeout_FastFailsAfterSpecifiedSeconds()
    {
        const int MaxInFlight = 1;
        const int WaitSeconds = 1;
        var limiter = new FoundryConcurrencyLimiter();
        using var heldLease = await limiter.AcquireAsync("test", MaxInFlight, WaitSeconds, CancellationToken.None);

        var start = DateTime.UtcNow;
        var act = async () => await limiter.AcquireAsync("test", MaxInFlight, WaitSeconds, CancellationToken.None);

        await act.Should().ThrowAsync<FoundryCapacityExceededException>();
        var elapsed = DateTime.UtcNow - start;
        elapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(WaitSeconds);
        elapsed.TotalSeconds.Should().BeLessThan(WaitSeconds + 0.5);
    }

    [Fact]
    public async Task CompleteAsync_PatentResearchTask_UsesExtendedCallTimeout()
    {
        const int DefaultTimeout = 80;
        const int PatentResearchTimeout = 150;
        var settings = new FoundrySettings
        {
            Endpoint = "https://test.openai.azure.com",
            Deployment = "gpt-5.5",
            ApiVersion = "preview",
            ApiKeySecretName = "test-key",
            CallTimeoutSeconds = DefaultTimeout,
            AcquireWaitSeconds = 2,
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                ["gpt-5.5"] = new() { MaxInFlight = 10 }
            },
            TaskOverrides = new Dictionary<string, FoundryTaskOverride>
            {
                ["patent_research"] = new() { CallTimeoutSeconds = PatentResearchTimeout }
            }
        };

        var httpClient = new HttpClient(new SlowResponseHandler(TimeSpan.FromSeconds(PatentResearchTimeout + 5)));
        var keyResolver = new FakeKeyResolver("test-api-key");
        var limiter = new FoundryConcurrencyLimiter();
        var provider = new FoundryProvider(
            Options.Create(settings),
            httpClient,
            keyResolver,
            limiter,
            NullLogger<FoundryProvider>.Instance);

        var request = new ModelRequest(
            ModelMode.SinglePrimary,
            "patent_research",
            "test prompt",
            null,
            new ModelOptions(1000, 1.0f),
            "gpt-5.5");

        var act = async () => await provider.CompleteAsync(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<FoundryTimeoutException>();
        exception.Which.Deployment.Should().Be("gpt-5.5");
        exception.Which.TimeoutSeconds.Should().Be(PatentResearchTimeout);
    }

    [Fact]
    public async Task CompleteAsync_GenericTask_UsesDefaultCallTimeout()
    {
        const int DefaultTimeout = 80;
        const int PatentResearchTimeout = 150;
        var settings = new FoundrySettings
        {
            Endpoint = "https://test.openai.azure.com",
            Deployment = "gpt-5.5",
            ApiVersion = "preview",
            ApiKeySecretName = "test-key",
            CallTimeoutSeconds = DefaultTimeout,
            AcquireWaitSeconds = 2,
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                ["gpt-5.5"] = new() { MaxInFlight = 10 }
            },
            TaskOverrides = new Dictionary<string, FoundryTaskOverride>
            {
                ["patent_research"] = new() { CallTimeoutSeconds = PatentResearchTimeout }
            }
        };

        var httpClient = new HttpClient(new SlowResponseHandler(TimeSpan.FromSeconds(DefaultTimeout + 5)));
        var keyResolver = new FakeKeyResolver("test-api-key");
        var limiter = new FoundryConcurrencyLimiter();
        var provider = new FoundryProvider(
            Options.Create(settings),
            httpClient,
            keyResolver,
            limiter,
            NullLogger<FoundryProvider>.Instance);

        var request = new ModelRequest(
            ModelMode.SinglePrimary,
            "generic_task",
            "test prompt",
            null,
            new ModelOptions(1000, 1.0f),
            "gpt-5.5");

        var act = async () => await provider.CompleteAsync(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<FoundryTimeoutException>();
        exception.Which.Deployment.Should().Be("gpt-5.5");
        exception.Which.TimeoutSeconds.Should().Be(DefaultTimeout);
    }

    [Fact]
    public void ResolveCallTimeout_PatentResearchTask_ReturnsTaskOverrideValue()
    {
        const int DefaultTimeout = 80;
        const int PatentResearchTimeout = 150;
        var settings = new FoundrySettings
        {
            CallTimeoutSeconds = DefaultTimeout,
            TaskOverrides = new Dictionary<string, FoundryTaskOverride>
            {
                ["patent_research"] = new() { CallTimeoutSeconds = PatentResearchTimeout }
            }
        };

        var result = settings.ResolveCallTimeout("patent_research");

        result.Should().Be(PatentResearchTimeout);
    }

    [Fact]
    public void ResolveCallTimeout_GenericTask_ReturnsDefaultValue()
    {
        const int DefaultTimeout = 80;
        const int PatentResearchTimeout = 150;
        var settings = new FoundrySettings
        {
            CallTimeoutSeconds = DefaultTimeout,
            TaskOverrides = new Dictionary<string, FoundryTaskOverride>
            {
                ["patent_research"] = new() { CallTimeoutSeconds = PatentResearchTimeout }
            }
        };

        var result = settings.ResolveCallTimeout("generic_task");

        result.Should().Be(DefaultTimeout);
    }

    private sealed class SlowResponseHandler : DelegatingHandler
    {
        private readonly TimeSpan _delay;

        public SlowResponseHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            try
            {
                await Task.Delay(_delay, ct);
            }
            catch (TaskCanceledException)
            {
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\": [], \"usage\": {\"prompt_tokens\": 1, \"completion_tokens\": 1, \"total_tokens\": 2}}")
            };
        }
    }

    private sealed class FakeKeyResolver : IProviderKeyResolver
    {
        private readonly string _key;

        public FakeKeyResolver(string key)
        {
            _key = key;
        }

        public Task<string> ResolveAsync(string secretName, CancellationToken ct)
        {
            return Task.FromResult(_key);
        }
    }
}
