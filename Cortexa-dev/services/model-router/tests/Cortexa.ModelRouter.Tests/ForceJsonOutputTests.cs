using System.Net;
using System.Text;
using System.Text.Json;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class ForceJsonOutputTests
{
    private const string SampleDeployment = "gpt-5-5-deployment";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static FoundrySettings BuildSettings() => new()
    {
        Endpoint = "https://cortexa-dev-ai-resource.services.ai.azure.com",
        Deployment = SampleDeployment,
        ApiVersion = "preview",
        ApiKeySecretName = "foundry-api-key"
    };

    private static ModelRequest BuildRequest(ModelOptions options) =>
        new(ModelMode.SinglePrimary, "extraction", "Extract entities from this text.", null, options, null);

    private static IProviderKeyResolver BuildKeyResolver()
    {
        var resolver = Substitute.For<IProviderKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("test-api-key");
        return resolver;
    }

    private static string SuccessResponseJson() =>
        """
        {
          "id": "resp-1",
          "model": "gpt-5.5",
          "choices": [ { "message": { "role": "assistant", "content": "{}" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """;

    private static (FoundryProvider Provider, CapturingHandler Handler) BuildProvider()
    {
        var handler = new CapturingHandler(SuccessResponseJson());
        var httpClient = new HttpClient(handler);
        var provider = new FoundryProvider(
            Options.Create(BuildSettings()), httpClient, BuildKeyResolver(), new FoundryConcurrencyLimiter(),
            NullLogger<FoundryProvider>.Instance);
        return (provider, handler);
    }

    [Fact]
    public async Task CompleteAsync_ForceJsonOutputTrue_IncludesResponseFormatInBody()
    {
        var (provider, handler) = BuildProvider();
        var options = new ModelOptions(ForceJsonOutput: true);

        await provider.CompleteAsync(BuildRequest(options));

        handler.CapturedBody.Should().NotBeNull();
        var root = handler.CapturedBody!.RootElement;
        root.TryGetProperty("response_format", out var responseFormat).Should().BeTrue();
        responseFormat.GetProperty("type").GetString().Should().Be("json_object");
    }

    [Fact]
    public async Task CompleteAsync_ForceJsonOutputFalse_OmitsResponseFormatFromBody()
    {
        var (provider, handler) = BuildProvider();
        var options = new ModelOptions(ForceJsonOutput: false);

        await provider.CompleteAsync(BuildRequest(options));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("response_format", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_ForceJsonOutputDefault_OmitsResponseFormatFromBody()
    {
        var (provider, handler) = BuildProvider();
        var options = new ModelOptions();

        await provider.CompleteAsync(BuildRequest(options));

        handler.CapturedBody.Should().NotBeNull();
        handler.CapturedBody!.RootElement.TryGetProperty("response_format", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_FoundryRequestWithResponseFormat_ProducesCorrectJsonShape()
    {
        var request = new FoundryRequest(
            "gpt-5.5",
            new[] { new FoundryMessage("user", "hi") },
            256,
            null,
            null,
            null,
            null,
            new FoundryResponseFormat("json_object"));

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        json.Should().Contain("\"response_format\"");
        json.Should().Contain("\"type\":\"json_object\"");
    }

    [Fact]
    public void Serialize_FoundryRequestWithNullResponseFormat_OmitsResponseFormatField()
    {
        var request = new FoundryRequest(
            "gpt-5.5",
            new[] { new FoundryMessage("user", "hi") },
            256,
            null,
            null,
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        json.Should().NotContain("response_format");
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
