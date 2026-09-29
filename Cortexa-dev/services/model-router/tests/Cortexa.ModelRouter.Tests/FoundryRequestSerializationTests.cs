using System.Text.Json;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry.Mapping;
using FluentAssertions;

namespace Cortexa.ModelRouter.Tests;

public sealed class FoundryRequestSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static FoundryRequest Sample(
        int? maxCompletionTokens = 256,
        float? temperature = null,
        bool? stream = null,
        string? reasoningEffort = null,
        int? maxTokens = null) =>
        new(
            "gpt-5.5",
            new[] { new FoundryMessage("user", "hi") },
            maxCompletionTokens,
            maxTokens,
            temperature,
            stream,
            reasoningEffort);

    [Fact]
    public void Serialize_UsesMaxCompletionTokens_NotMaxTokens()
    {
        var json = JsonSerializer.Serialize(Sample(maxCompletionTokens: 256), Options);

        json.Should().Contain("\"max_completion_tokens\":256");
        json.Should().NotContain("max_tokens");
    }

    [Fact]
    public void Serialize_WithNullTemperature_OmitsTemperature()
    {
        var json = JsonSerializer.Serialize(Sample(temperature: null), Options);

        json.Should().NotContain("temperature");
    }

    [Fact]
    public void Serialize_WithDefaultTemperature_IncludesTemperature()
    {
        var json = JsonSerializer.Serialize(Sample(temperature: 1.0f), Options);

        json.Should().Contain("\"temperature\":1");
    }

    [Fact]
    public void Serialize_WithNullStream_OmitsStream()
    {
        var json = JsonSerializer.Serialize(Sample(stream: null), Options);

        json.Should().NotContain("stream");
    }

    [Fact]
    public void Serialize_WithReasoningEffortSet_IncludesReasoningEffort()
    {
        const string ExpectedReasoningEffort = "high";
        var json = JsonSerializer.Serialize(Sample(reasoningEffort: ExpectedReasoningEffort), Options);

        json.Should().Contain("\"reasoning_effort\":\"high\"");
    }

    [Fact]
    public void Serialize_WithNullReasoningEffort_OmitsReasoningEffort()
    {
        var json = JsonSerializer.Serialize(Sample(reasoningEffort: null), Options);

        json.Should().NotContain("reasoning_effort");
    }

    [Fact]
    public void Serialize_UsesMaxTokens_NotMaxCompletionTokens()
    {
        var json = JsonSerializer.Serialize(
            Sample(maxCompletionTokens: null, maxTokens: 512), Options);

        json.Should().Contain("\"max_tokens\":512");
        json.Should().NotContain("max_completion_tokens");
    }

    [Fact]
    public void Serialize_WithBothMaxTokenFieldsNull_OmitsBoth()
    {
        var json = JsonSerializer.Serialize(
            Sample(maxCompletionTokens: null, maxTokens: null), Options);

        json.Should().NotContain("max_completion_tokens");
        json.Should().NotContain("max_tokens");
    }

    [Fact]
    public void Serialize_WithOnlyMaxCompletionTokensPopulated_OmitsMaxTokens()
    {
        var json = JsonSerializer.Serialize(
            Sample(maxCompletionTokens: 128, maxTokens: null), Options);

        json.Should().Contain("\"max_completion_tokens\":128");
        json.Should().NotContain("max_tokens\":");
    }
}
