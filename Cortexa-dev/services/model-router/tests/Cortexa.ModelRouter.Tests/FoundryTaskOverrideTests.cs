using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cortexa.ModelRouter.Application.DTOs;
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

public sealed class FoundryTaskOverrideTests
{
    private const string SampleApiKey = "test-api-key";
    private const string SampleDeployment = "gpt-5-5-deployment";
    private static readonly ModelOptions DefaultOptions = new(8192, null, false);

    private static FoundrySettings BuildSettingsWithTaskOverrides(
        Dictionary<string, FoundryTaskOverride> taskOverrides) => new()
        {
            Endpoint = "https://cortexa-dev-ai-resource.services.ai.azure.com",
            Deployment = SampleDeployment,
            ApiVersion = "preview",
            ApiKeySecretName = "foundry-api-key",
            ReasoningEffort = "high",
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                [SampleDeployment] = new()
                {
                    TokenFieldKind = FoundryTokenFieldKind.MaxCompletionTokens,
                    SendReasoningEffort = true,
                    SendTemperature = true,
                    MaxInFlight = 24
                }
            },
            TaskOverrides = taskOverrides
        };

    private static ModelRequest BuildRequest(string taskKind) =>
        new(ModelMode.SinglePrimary, taskKind, "Analyze X", null, DefaultOptions, null);

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
    public async Task CompleteAsync_PatentResearchTaskKind_AppliesMediumReasoningEffortAndTaskTokenCap()
    {
        const string TaskKind = "patent_research";
        const int TaskTokenCap = 4096;
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "medium", OutputTokenCap = TaskTokenCap }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be("medium");
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(TaskTokenCap);
    }

    [Fact]
    public async Task CompleteAsync_DifferentTaskKind_UsesGlobalHighReasoningEffortAndNoTaskCap()
    {
        const string RegisteredTaskKind = "patent_research";
        const string UnregisteredTaskKind = "generic_analysis";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [RegisteredTaskKind] = new() { ReasoningEffort = "medium", OutputTokenCap = 4096 }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(UnregisteredTaskKind));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be("high");
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(8192);
    }

    [Fact]
    public void ResolveCallTimeout_PatentResearchTaskKindFromAppSettings_Returns40Seconds()
    {
        const string TaskKind = "patent_research";
        const int ExpectedCallTimeoutSeconds = 40;
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();
        var settings = configuration.GetSection("Foundry").Get<FoundrySettings>();

        var callTimeout = settings!.ResolveCallTimeout(TaskKind);

        callTimeout.Should().Be(ExpectedCallTimeoutSeconds);
    }

    private static string AppSettingsPath([CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "..", "..", "src", "Api", "appsettings.json");

    [Fact]
    public void ResolveTaskOverride_MissingTaskKind_ReturnsNull()
    {
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            ["patent_research"] = new() { ReasoningEffort = "medium", OutputTokenCap = 4096 }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);

        var result = settings.ResolveTaskOverride("nonexistent_task");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_TaskCapAndDeploymentCapBothPresent_AppliesMinimum()
    {
        const string TaskKind = "patent_research";
        const int DeploymentCap = 8192;
        const int TaskCap = 4096;
        const int RequestedTokens = 10000;
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "medium", OutputTokenCap = TaskCap }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);
        settings.DeploymentOptions[SampleDeployment].OutputTokenCap = DeploymentCap;
        var (provider, handler) = BuildProvider(settings);
        var requestOptions = new ModelOptions(RequestedTokens, null, false);
        var request = new ModelRequest(ModelMode.SinglePrimary, TaskKind, "Analyze X", null, requestOptions, null);

        await provider.CompleteAsync(request);

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("max_completion_tokens").GetInt32().Should().Be(TaskCap);
    }

    [Fact]
    public async Task CompleteAsync_TaskKindEmpty_UsesGlobalReasoningEffortAndNoTaskCap()
    {
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            ["patent_research"] = new() { ReasoningEffort = "medium", OutputTokenCap = 4096 }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);
        var (provider, handler) = BuildProvider(settings);
        var request = new ModelRequest(ModelMode.SinglePrimary, string.Empty, "Analyze X", null, DefaultOptions, null);

        await provider.CompleteAsync(request);

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("reasoning_effort").GetString().Should().Be("high");
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(8192);
    }

    [Fact]
    public async Task CompleteAsync_SendReasoningEffortFalse_OmitsReasoningEffortEvenWithTaskOverride()
    {
        const string TaskKind = "patent_research";
        var taskOverrides = new Dictionary<string, FoundryTaskOverride>
        {
            [TaskKind] = new() { ReasoningEffort = "medium", OutputTokenCap = 4096 }
        };
        var settings = BuildSettingsWithTaskOverrides(taskOverrides);
        settings.DeploymentOptions[SampleDeployment].SendReasoningEffort = false;
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(TaskKind));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson)
        {
            _responseJson = responseJson;
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

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
