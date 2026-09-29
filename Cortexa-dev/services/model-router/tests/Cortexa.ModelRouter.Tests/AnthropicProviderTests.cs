using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Anthropic;
using Cortexa.ModelRouter.Infrastructure.Providers.Anthropic.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class AnthropicProviderTests
{
    private readonly HttpClient _http;
    private readonly IProviderKeyResolver _keyResolver;

    public AnthropicProviderTests()
    {
        _http = new HttpClient();
        _keyResolver = Substitute.For<IProviderKeyResolver>();
    }

    [Fact]
    public void BuildHttpRequest_WithApiVersion_AppendsApiVersionQueryParam()
    {
        var settings = new AnthropicSettings
        {
            BaseUrl = "https://cortexa-dev-ai-resource.services.ai.azure.com/anthropic",
            Model = "claude-opus-4-8",
            AnthropicVersion = "2023-06-01",
            ApiVersion = "2025-05-01-preview"
        };
        var provider = new AnthropicProvider(Options.Create(settings), _http, _keyResolver);
        var body = new AnthropicRequest(
            settings.Model,
            4096,
            new[] { new AnthropicMessage("user", "test") },
            null);

        var request = provider.BuildHttpRequest("test-key", body);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.ToString().Should().Be("https://cortexa-dev-ai-resource.services.ai.azure.com/anthropic/v1/messages?api-version=2025-05-01-preview");
    }

    [Fact]
    public void BuildHttpRequest_WithEmptyApiVersion_OmitsQueryParam()
    {
        var settings = new AnthropicSettings
        {
            BaseUrl = "https://api.anthropic.com",
            Model = "claude-sonnet-4-6",
            AnthropicVersion = "2023-06-01",
            ApiVersion = ""
        };
        var provider = new AnthropicProvider(Options.Create(settings), _http, _keyResolver);
        var body = new AnthropicRequest(
            settings.Model,
            4096,
            new[] { new AnthropicMessage("user", "test") },
            null);

        var request = provider.BuildHttpRequest("test-key", body);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
    }

    [Fact]
    public void BuildHttpRequest_UsesApiKeyHeader()
    {
        var settings = new AnthropicSettings
        {
            BaseUrl = "https://cortexa-dev-ai-resource.services.ai.azure.com/anthropic",
            Model = "claude-opus-4-8",
            AnthropicVersion = "2023-06-01",
            ApiVersion = "2025-05-01-preview"
        };
        var provider = new AnthropicProvider(Options.Create(settings), _http, _keyResolver);
        var body = new AnthropicRequest(
            settings.Model,
            4096,
            new[] { new AnthropicMessage("user", "test") },
            null);

        var request = provider.BuildHttpRequest("test-api-key", body);

        request.Headers.Should().Contain(h => h.Key == "api-key" && h.Value.First() == "test-api-key");
        request.Headers.Should().NotContain(h => h.Key == "x-api-key");
    }

    [Fact]
    public void BuildHttpRequest_PreservesAnthropicVersionHeader()
    {
        var settings = new AnthropicSettings
        {
            BaseUrl = "https://cortexa-dev-ai-resource.services.ai.azure.com/anthropic",
            Model = "claude-opus-4-8",
            AnthropicVersion = "2023-06-01",
            ApiVersion = "2025-05-01-preview"
        };
        var provider = new AnthropicProvider(Options.Create(settings), _http, _keyResolver);
        var body = new AnthropicRequest(
            settings.Model,
            4096,
            new[] { new AnthropicMessage("user", "test") },
            null);

        var request = provider.BuildHttpRequest("test-key", body);

        request.Headers.Should().Contain(h => h.Key == "anthropic-version" && h.Value.First() == "2023-06-01");
    }
}
