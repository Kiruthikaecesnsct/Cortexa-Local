using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class FoundryReasoningEffortTests
{
    private const string SampleApiKey = "test-api-key";
    private const string Gpt55Deployment = "gpt-5.5";
    private const string Gpt54Deployment = "gpt-5.4";
    private const string Grok43Deployment = "grok-4.3";
    private const string DeepSeekDeployment = "DeepSeek-V4-Pro";
    private static readonly ModelOptions DefaultOptions = new(8192, null, false);

    private static FoundrySettings BuildSettings(
        string deployment,
        FoundryDeploymentOptions deploymentOptions,
        Dictionary<string, FoundryTaskOverride>? taskOverrides = null) => new()
        {
            Endpoint = "https://cortexa-dev-ai-resource.services.ai.azure.com",
            Deployment = deployment,
            ApiVersion = "v1",
            ApiKeySecretName = "foundry-api-key",
            ReasoningEffort = "high",
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                [deployment] = deploymentOptions
            },
            TaskOverrides = taskOverrides ?? new Dictionary<string, FoundryTaskOverride>()
        };

    private static ModelRequest BuildRequest(string taskKind, string model) =>
        new(ModelMode.SinglePrimary, taskKind, "Analyze X", null, DefaultOptions, model);

    private static IProviderKeyResolver BuildKeyResolver()
    {
        var resolver = Substitute.For<IProviderKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SampleApiKey);
        return resolver;
    }

    private static string SuccessResponseJson() =>
        """
        {
          "id": "resp-1",
          "model": "gpt-5.5",
          "choices": [ { "message": { "role": "assistant", "content": "hello" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 2, "total_tokens": 3 }
        }
        """;

    private static (FoundryProvider provider, CapturingHandler handler) BuildProvider(FoundrySettings settings)
    {
        var handler = new CapturingHandler(SuccessResponseJson());
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(settings), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);
        return (provider, handler);
    }

    [Fact]
    public async Task CompleteAsync_MinimalEffort_Gpt55Deployment_CoercesToLow()
    {
        const string TaskKind = "patent_research";
        const string ExpectedCoercedEffort = "low";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "minimal" }
        };
        var gpt55Options = new FoundryDeploymentOptions
        {
            SendReasoningEffort = true,
            MaxInFlight = 24,
            SupportedReasoningEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" }
        };
        var settings = BuildSettings(Gpt55Deployment, gpt55Options, taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind, Gpt55Deployment));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be(ExpectedCoercedEffort);
    }

    [Fact]
    public async Task CompleteAsync_MinimalEffort_Gpt54Deployment_PassesThroughUnchanged()
    {
        const string TaskKind = "patent_research";
        const string ExpectedEffort = "minimal";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = ExpectedEffort }
        };
        var gpt54Options = new FoundryDeploymentOptions
        {
            SendReasoningEffort = true,
            MaxInFlight = 24,
            SupportedReasoningEfforts = new List<string> { "none", "minimal", "low", "medium", "high", "xhigh" }
        };
        var settings = BuildSettings(Gpt54Deployment, gpt54Options, taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind, Gpt54Deployment));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be(ExpectedEffort);
    }

    [Fact]
    public async Task CompleteAsync_NoSupportedEffortsConfigured_PassesThroughEffortUnchanged()
    {
        const string TaskKind = "patent_research";
        const string RequestedEffort = "minimal";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = RequestedEffort }
        };
        var grokOptions = new FoundryDeploymentOptions
        {
            SendReasoningEffort = true,
            MaxInFlight = 10,
            SupportedReasoningEfforts = null
        };
        var settings = BuildSettings(Grok43Deployment, grokOptions, taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind, Grok43Deployment));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be(RequestedEffort);
    }

    [Fact]
    public async Task CompleteAsync_SendReasoningEffortFalse_OmitsEffortRegardlessOfSupported()
    {
        const string TaskKind = "patent_research";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "minimal" }
        };
        var deepSeekOptions = new FoundryDeploymentOptions
        {
            SendReasoningEffort = false,
            MaxInFlight = 10,
            SupportedReasoningEfforts = null
        };
        var settings = BuildSettings(DeepSeekDeployment, deepSeekOptions, taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind, DeepSeekDeployment));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public void ReasoningEffortResolver_NullRequestedEffort_ReturnsNull()
    {
        var supportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(null, supportedEfforts);

        result.Should().BeNull();
    }

    [Fact]
    public void ReasoningEffortResolver_EmptyRequestedEffort_ReturnsNull()
    {
        var supportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(string.Empty, supportedEfforts);

        result.Should().BeNull();
    }

    [Fact]
    public void ReasoningEffortResolver_WhitespaceRequestedEffort_ReturnsNull()
    {
        var supportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve("   ", supportedEfforts);

        result.Should().BeNull();
    }

    [Fact]
    public void ReasoningEffortResolver_NullSupportedList_PassesThroughRequested()
    {
        const string RequestedEffort = "minimal";

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, null);

        result.Should().Be(RequestedEffort);
    }

    [Fact]
    public void ReasoningEffortResolver_EmptySupportedList_PassesThroughRequested()
    {
        const string RequestedEffort = "minimal";

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, new List<string>());

        result.Should().Be(RequestedEffort);
    }

    [Fact]
    public void ReasoningEffortResolver_RequestedIsAlreadySupported_ReturnsUnchanged()
    {
        const string RequestedEffort = "minimal";
        var supportedEfforts = new List<string> { "none", "minimal", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, supportedEfforts);

        result.Should().Be(RequestedEffort);
    }

    [Fact]
    public void ReasoningEffortResolver_MinimalWithGpt55SupportedList_CoercesToLow()
    {
        const string RequestedEffort = "minimal";
        const string ExpectedCoerced = "low";
        var gpt55SupportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, gpt55SupportedEfforts);

        result.Should().Be(ExpectedCoerced);
    }

    [Fact]
    public void ReasoningEffortResolver_TieDistanceRoundsUp()
    {
        const string RequestedEffort = "minimal";
        const string ExpectedRoundedUp = "low";
        var supportedEfforts = new List<string> { "none", "low", "medium" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, supportedEfforts);

        result.Should().Be(ExpectedRoundedUp);
    }

    [Fact]
    public void ReasoningEffortResolver_UnknownRequestedString_ReturnsFirstSupported()
    {
        const string UnknownEffort = "ultra-high";
        var supportedEfforts = new List<string> { "low", "medium", "high" };
        var expectedFallback = supportedEfforts[0];

        var result = ReasoningEffortResolver.Resolve(UnknownEffort, supportedEfforts);

        result.Should().Be(expectedFallback);
    }

    [Fact]
    public void ReasoningEffortResolver_CaseInsensitiveMatching()
    {
        const string RequestedEffortUpperCase = "MINIMAL";
        var supportedEfforts = new List<string> { "none", "minimal", "low", "medium" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffortUpperCase, supportedEfforts);

        result.Should().Be("MINIMAL");
    }

    [Fact]
    public void ReasoningEffortResolver_HighToMediumCoercion()
    {
        const string RequestedEffort = "high";
        const string ExpectedCoerced = "medium";
        var supportedEfforts = new List<string> { "none", "minimal", "low", "medium" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, supportedEfforts);

        result.Should().Be(ExpectedCoerced);
    }

    [Fact]
    public void ReasoningEffortResolver_NoneRequested_PassesThrough()
    {
        const string RequestedEffort = "none";
        var supportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, supportedEfforts);

        result.Should().Be(RequestedEffort);
    }

    [Fact]
    public void ReasoningEffortResolver_XhighRequested_PassesThrough()
    {
        const string RequestedEffort = "xhigh";
        var supportedEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" };

        var result = ReasoningEffortResolver.Resolve(RequestedEffort, supportedEfforts);

        result.Should().Be(RequestedEffort);
    }

    [Fact]
    public async Task CompleteAsync_InvalidRequestError_SurfacesDeploymentTaskKindAndParam()
    {
        const string TaskKind = "patent_research";
        const string OffendingParam = "reasoning_effort";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "minimal" }
        };
        var gpt55Options = new FoundryDeploymentOptions
        {
            SendReasoningEffort = true,
            MaxInFlight = 24,
            SupportedReasoningEfforts = new List<string> { "none", "low", "medium", "high", "xhigh" }
        };
        var settings = BuildSettings(Gpt55Deployment, gpt55Options, taskOverrides);

        var errorBody = $$"""
        {
          "error": {
            "code": "invalid_request_error",
            "message": "Invalid value for parameter 'reasoning_effort'",
            "param": "{{OffendingParam}}"
          }
        }
        """;
        var handler = new CapturingHandler(errorBody, HttpStatusCode.BadRequest);
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(settings), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);

        var exception = await Assert.ThrowsAsync<ModelProviderException>(
            async () => await provider.CompleteAsync(BuildRequest(TaskKind, Gpt55Deployment)));

        exception.HttpStatusCode.Should().Be(400);
        exception.Message.Should().Contain("invalid_request_error");
        exception.Message.Should().Contain(OffendingParam);
    }

    [Fact]
    public void AppSettings_Gpt55DeploymentOptions_DoesNotSupportMinimal()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();
        var settings = configuration.GetSection("Foundry").Get<FoundrySettings>();

        var gpt55Options = settings!.ResolveOptions("gpt-5.5");

        gpt55Options.SupportedReasoningEfforts.Should().NotBeNull();
        gpt55Options.SupportedReasoningEfforts.Should().NotContain("minimal");
        gpt55Options.SupportedReasoningEfforts.Should().Contain(new[] { "none", "low", "medium", "high", "xhigh" });
    }

    [Fact]
    public void AppSettings_Gpt54DeploymentOptions_SupportsMinimal()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();
        var settings = configuration.GetSection("Foundry").Get<FoundrySettings>();

        var gpt54Options = settings!.ResolveOptions("gpt-5.4");

        gpt54Options.SupportedReasoningEfforts.Should().NotBeNull();
        gpt54Options.SupportedReasoningEfforts.Should().Contain("minimal");
        gpt54Options.SupportedReasoningEfforts.Should().Contain(new[] { "none", "low", "medium", "high", "xhigh" });
    }

    [Fact]
    public void AppSettings_Grok43DeploymentOptions_NoSupportedReasoningEfforts()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();
        var settings = configuration.GetSection("Foundry").Get<FoundrySettings>();

        var grokOptions = settings!.ResolveOptions("grok-4.3");

        grokOptions.SendReasoningEffort.Should().BeFalse();
        grokOptions.SupportedReasoningEfforts.Should().BeNull();
    }

    [Fact]
    public void AppSettings_DeepSeekDeploymentOptions_NoSupportedReasoningEfforts()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();
        var settings = configuration.GetSection("Foundry").Get<FoundrySettings>();

        var deepSeekOptions = settings!.ResolveOptions("DeepSeek-V4-Pro");

        deepSeekOptions.SendReasoningEffort.Should().BeFalse();
        deepSeekOptions.SupportedReasoningEfforts.Should().BeNull();
    }

    private static string AppSettingsPath([CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "..", "..", "src", "Api", "appsettings.json");

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public CapturingHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        public JsonDocument? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                CapturedBody = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
