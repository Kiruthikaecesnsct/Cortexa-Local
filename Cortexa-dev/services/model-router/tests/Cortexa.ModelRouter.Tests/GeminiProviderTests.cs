using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Gemini;
using Cortexa.ModelRouter.Infrastructure.Providers.Gemini.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class GeminiProviderTests
{
    private readonly HttpClient _http;
    private readonly IProviderKeyResolver _keyResolver;

    public GeminiProviderTests()
    {
        _http = new HttpClient();
        _keyResolver = Substitute.For<IProviderKeyResolver>();
    }

    private static GeminiSettings BuildSettings() => new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com",
        ApiVersion = "v1beta",
        Model = "gemini-3.1-pro",
        ApiKeySecretName = "gemini-api-key"
    };

    [Fact]
    public void BuildHttpRequest_NonStreaming_BuildsGenerateContentUrl()
    {
        var settings = BuildSettings();
        var provider = new GeminiProvider(Options.Create(settings), _http, _keyResolver);
        var body = new GeminiRequest(new[] { new GeminiContent("user", new[] { new GeminiPart("test") }) }, null);

        var request = provider.BuildHttpRequest("test-key", settings.Model, body, stream: false);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro:generateContent");
    }

    [Fact]
    public void BuildHttpRequest_Streaming_AppendsAltSseQueryParam()
    {
        var settings = BuildSettings();
        var provider = new GeminiProvider(Options.Create(settings), _http, _keyResolver);
        var body = new GeminiRequest(new[] { new GeminiContent("user", new[] { new GeminiPart("test") }) }, null);

        var request = provider.BuildHttpRequest("test-key", settings.Model, body, stream: true);

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro:streamGenerateContent?alt=sse");
    }

    [Fact]
    public void BuildHttpRequest_UsesGoogApiKeyHeader()
    {
        var settings = BuildSettings();
        var provider = new GeminiProvider(Options.Create(settings), _http, _keyResolver);
        var body = new GeminiRequest(new[] { new GeminiContent("user", new[] { new GeminiPart("test") }) }, null);

        var request = provider.BuildHttpRequest("test-api-key", settings.Model, body, stream: false);

        request.Headers.Should().Contain(h => h.Key == "x-goog-api-key" && h.Value.First() == "test-api-key");
    }

    [Fact]
    public void BuildHttpRequest_ExplicitModel_OverridesConfiguredDefault()
    {
        var settings = BuildSettings();
        var provider = new GeminiProvider(Options.Create(settings), _http, _keyResolver);
        var body = new GeminiRequest(new[] { new GeminiContent("user", new[] { new GeminiPart("test") }) }, null);

        var request = provider.BuildHttpRequest("test-key", "gemini-3.8-flash", body, stream: false);

        request.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");
    }
}
