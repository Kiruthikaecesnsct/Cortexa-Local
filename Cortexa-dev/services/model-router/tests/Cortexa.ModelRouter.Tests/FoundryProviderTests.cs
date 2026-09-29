using System.Net;
using System.Text;
using System.Text.Json;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class FoundryProviderTests
{
    private const string SampleApiKey = "test-api-key";
    private const string SampleDeployment = "gpt-5-5-deployment";
    private static readonly ModelOptions DefaultOptions = new();

    private static FoundrySettings BuildSettings(string? reasoningEffort) => new()
    {
        Endpoint = "https://cortexa-dev-ai-resource.services.ai.azure.com",
        Deployment = SampleDeployment,
        ApiVersion = "preview",
        ApiKeySecretName = "foundry-api-key",
        ReasoningEffort = reasoningEffort
    };

    private static ModelRequest BuildRequest(string? model = null) =>
        new(ModelMode.SinglePrimary, "analysis", "What is X?", null, DefaultOptions, model);

    private static ModelRequest BuildRequest(string? model, ModelOptions modelOptions) =>
        new(ModelMode.SinglePrimary, "analysis", "What is X?", null, modelOptions, model);

    private static FoundrySettings BuildSettingsWithDeploymentOptions(
        string deployment,
        FoundryDeploymentOptions deploymentOptions,
        string? reasoningEffort = "high") => new()
        {
            Endpoint = "https://cortexa-dev-ai-resource.services.ai.azure.com",
            Deployment = deployment,
            ApiVersion = "preview",
            ApiKeySecretName = "foundry-api-key",
            ReasoningEffort = reasoningEffort,
            DeploymentOptions = new Dictionary<string, FoundryDeploymentOptions>
            {
                [deployment] = deploymentOptions
            }
        };

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
    public async Task CompleteAsync_ReasoningEffortConfigured_IncludesReasoningEffortInBody()
    {
        const string ExpectedReasoningEffort = "high";
        var (provider, handler) = BuildProvider(BuildSettings(ExpectedReasoningEffort));

        await provider.CompleteAsync(BuildRequest());

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("reasoning_effort").GetString().Should().Be(ExpectedReasoningEffort);
    }

    [Fact]
    public async Task CompleteAsync_ReasoningEffortNull_OmitsReasoningEffortFromBody()
    {
        var (provider, handler) = BuildProvider(BuildSettings(reasoningEffort: null));

        await provider.CompleteAsync(BuildRequest());

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_RequestModelSet_UsesRequestModelAsOutboundModel()
    {
        const string ExplicitModel = "gpt-5-4-eastus-deployment";
        var (provider, handler) = BuildProvider(BuildSettings(reasoningEffort: null));

        await provider.CompleteAsync(BuildRequest(ExplicitModel));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("model").GetString().Should().Be(ExplicitModel);
    }

    [Fact]
    public async Task CompleteAsync_RequestModelAbsent_FallsBackToSettingsDeployment()
    {
        var (provider, handler) = BuildProvider(BuildSettings(reasoningEffort: null));

        await provider.CompleteAsync(BuildRequest());

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("model").GetString().Should().Be(SampleDeployment);
    }

    [Fact]
    public async Task CompleteAsync_GrokShapedDeployment_UsesMaxTokensNotMaxCompletionTokens()
    {
        const string GrokDeployment = "grok-4-3-deployment";
        const int RequestedMaxTokens = 2048;
        var deploymentOptions = new FoundryDeploymentOptions
        {
            TokenFieldKind = FoundryTokenFieldKind.MaxTokens,
            SendReasoningEffort = false,
            SendTemperature = false,
            OutputTokenCap = 8192,
            MaxInFlight = 10
        };
        var settings = BuildSettingsWithDeploymentOptions(GrokDeployment, deploymentOptions);
        var (provider, handler) = BuildProvider(settings);
        var requestOptions = new ModelOptions(RequestedMaxTokens, null, false);

        await provider.CompleteAsync(BuildRequest(GrokDeployment, requestOptions));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(RequestedMaxTokens);
        handler.CapturedBody!.RootElement.TryGetProperty("max_completion_tokens", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_SendTemperatureFalse_OmitsTemperatureEvenAtSupportedValue()
    {
        const string GrokDeployment = "grok-4-3-deployment";
        const float SupportedTemperature = 1.0f;
        var deploymentOptions = new FoundryDeploymentOptions
        {
            TokenFieldKind = FoundryTokenFieldKind.MaxTokens,
            SendReasoningEffort = false,
            SendTemperature = false,
            OutputTokenCap = 8192,
            MaxInFlight = 10
        };
        var settings = BuildSettingsWithDeploymentOptions(GrokDeployment, deploymentOptions);
        var (provider, handler) = BuildProvider(settings);
        var requestOptions = new ModelOptions(256, SupportedTemperature, false);

        await provider.CompleteAsync(BuildRequest(GrokDeployment, requestOptions));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("temperature", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_SendReasoningEffortFalse_OmitsReasoningEffortEvenWhenSettingsHigh()
    {
        const string GrokDeployment = "grok-4-3-deployment";
        var deploymentOptions = new FoundryDeploymentOptions
        {
            TokenFieldKind = FoundryTokenFieldKind.MaxTokens,
            SendReasoningEffort = false,
            SendTemperature = false,
            OutputTokenCap = 8192,
            MaxInFlight = 10
        };
        var settings = BuildSettingsWithDeploymentOptions(GrokDeployment, deploymentOptions, reasoningEffort: "high");
        var (provider, handler) = BuildProvider(settings);

        await provider.CompleteAsync(BuildRequest(GrokDeployment));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_RequestedMaxTokensAboveOutputTokenCap_ClampsToCap()
    {
        const string GrokDeployment = "grok-4-3-deployment";
        const int OutputTokenCap = 8192;
        const int RequestedMaxTokens = 20000;
        var deploymentOptions = new FoundryDeploymentOptions
        {
            TokenFieldKind = FoundryTokenFieldKind.MaxTokens,
            SendReasoningEffort = false,
            SendTemperature = false,
            OutputTokenCap = OutputTokenCap,
            MaxInFlight = 10
        };
        var settings = BuildSettingsWithDeploymentOptions(GrokDeployment, deploymentOptions);
        var (provider, handler) = BuildProvider(settings);
        var requestOptions = new ModelOptions(RequestedMaxTokens, null, false);

        await provider.CompleteAsync(BuildRequest(GrokDeployment, requestOptions));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(OutputTokenCap);
    }

    [Fact]
    public async Task CompleteAsync_DeploymentNotInDeploymentOptions_FallsBackToGptLikeDefaults()
    {
        const string ExpectedReasoningEffort = "high";
        const float SupportedTemperature = 1.0f;
        var settings = BuildSettings(ExpectedReasoningEffort);
        var (provider, handler) = BuildProvider(settings);
        var requestOptions = new ModelOptions(256, SupportedTemperature, false);

        await provider.CompleteAsync(BuildRequest(null, requestOptions));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(256);
        root.TryGetProperty("max_tokens", out _).Should().BeFalse();
        root.GetProperty("reasoning_effort").GetString().Should().Be(ExpectedReasoningEffort);
        root.GetProperty("temperature").GetSingle().Should().Be(SupportedTemperature);
    }

    [Fact]
    public async Task CompleteAsync_ChoiceHasFinishReason_PopulatesFinishReasonOnResult()
    {
        var (provider, _) = BuildProvider(BuildSettings(reasoningEffort: null));

        var result = await provider.CompleteAsync(BuildRequest());

        result.FinishReason.Should().Be("stop");
    }

    [Fact]
    public async Task CompleteAsync_ChoiceFinishReasonNull_ResultFinishReasonIsNull()
    {
        const string NullFinishReasonJson =
            """
            {
              "id": "resp-2",
              "model": "gpt-5.5",
              "choices": [ { "message": { "role": "assistant", "content": "hello" }, "finish_reason": null } ],
              "usage": { "prompt_tokens": 1, "completion_tokens": 2, "total_tokens": 3 }
            }
            """;
        var handler = new CapturingHandler(NullFinishReasonJson);
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(BuildSettings(reasoningEffort: null)), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);

        var result = await provider.CompleteAsync(BuildRequest());

        result.FinishReason.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_EmptyChoices_ResultFinishReasonIsNull()
    {
        const string EmptyChoicesJson =
            """
            {
              "id": "resp-3",
              "model": "gpt-5.5",
              "choices": [],
              "usage": { "prompt_tokens": 1, "completion_tokens": 0, "total_tokens": 1 }
            }
            """;
        var handler = new CapturingHandler(EmptyChoicesJson);
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(BuildSettings(reasoningEffort: null)), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);

        var result = await provider.CompleteAsync(BuildRequest());

        result.FinishReason.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_UpstreamReturns500_ThrowsModelProviderExceptionWithStatusCode()
    {
        var handler = new SequenceHandler(new[] { HttpStatusCode.InternalServerError }, SuccessResponseJson());
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(BuildSettings(reasoningEffort: null)), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);

        var act = async () => await provider.CompleteAsync(BuildRequest());

        var exception = await act.Should().ThrowAsync<ModelProviderException>();
        exception.Which.HttpStatusCode.Should().Be(500);
        exception.Which.ProviderName.Should().Be("Foundry");
    }

    [Fact]
    public async Task CompleteAsync_ResiliencePipelineTransientFailureTwiceThenSuccess_RetriesAndReturnsResult()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Foundry:Endpoint"] = "https://test.openai.azure.com",
                ["Foundry:Deployment"] = SampleDeployment,
                ["Foundry:ApiVersion"] = "v1",
                ["Foundry:ApiKeySecretName"] = "test-key",
                ["Foundry:TimeoutSeconds"] = "30",
                ["Foundry:CallTimeoutSeconds"] = "20",
                ["Foundry:MaxRetries"] = "3",
                ["Foundry:RetryBaseDelayMs"] = "1",
                ["Anthropic:BaseUrl"] = "https://test.anthropic.local",
                ["Anthropic:Model"] = "claude-test",
                ["Anthropic:AnthropicVersion"] = "2023-06-01",
                ["Anthropic:ApiKeySecretName"] = "anthropic-key",
                ["KeyResolver:SecretCacheTtlSeconds"] = "3600"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModelRouterInfrastructure(configuration);
        services.AddSingleton(BuildKeyResolver());
        var sequenceHandler = new SequenceHandler(
            new[] { HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK },
            SuccessResponseJson());
        services.AddHttpClient<FoundryProvider>().ConfigurePrimaryHttpMessageHandler(() => sequenceHandler);

        await using var provider = services.BuildServiceProvider();
        var foundryProvider = provider.GetRequiredService<FoundryProvider>();

        var result = await foundryProvider.CompleteAsync(BuildRequest());

        result.Content.Should().Be("hello");
        sequenceHandler.CallCount.Should().Be(3);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statusSequence;
        private readonly string _successBody;

        public SequenceHandler(IEnumerable<HttpStatusCode> statusSequence, string successBody)
        {
            _statusSequence = new Queue<HttpStatusCode>(statusSequence);
            _successBody = successBody;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var status = _statusSequence.Count > 0 ? _statusSequence.Dequeue() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            response.Content = status == HttpStatusCode.OK
                ? new StringContent(_successBody, Encoding.UTF8, "application/json")
                : new StringContent("upstream error", Encoding.UTF8, "text/plain");
            return Task.FromResult(response);
        }
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
