using Collector.Domain.Enums;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class GeminiProviderOptionsValidatorTests
{
    private readonly GeminiProviderOptionsValidator _validator = new();

    private static string Failures(Microsoft.Extensions.Options.ValidateOptionsResult result) =>
        string.Join('\n', result.Failures ?? []);

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        Assert.True(_validator.Validate(null, new GeminiProviderOptions()).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Validate_KnownThinkingLevel_Succeeds(string level)
    {
        Assert.True(_validator.Validate(null, new GeminiProviderOptions { Thinking = level }).Succeeded);
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("between_tools")]
    public void Validate_UnknownThinkingLevel_Fails(string level)
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { Thinking = level });

        Assert.Contains("Ai:Gemini:Thinking", Failures(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_InvalidNumbersAndModel_ReportsEveryField()
    {
        var options = new GeminiProviderOptions
        {
            Model = " ",
            MaxOutputTokens = 0,
            Concurrency = 0,
            MaxRetries = -1,
            TimeoutSeconds = 0,
            Temperature = 2.5,
        };

        var failures = Failures(_validator.Validate(null, options));

        foreach (var field in new[] { "Model", "MaxOutputTokens", "Concurrency", "MaxRetries", "TimeoutSeconds", "Temperature" })
        {
            Assert.Contains($"Ai:Gemini:{field}", failures, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(CollectorProvider.Claude)]
    [InlineData(CollectorProvider.Gemini)]
    public void AiOptions_SupportedProvider_Succeeds(CollectorProvider provider)
    {
        Assert.True(new AiOptionsValidator().Validate(null, new AiOptions { Provider = provider }).Succeeded);
    }

    [Fact]
    public void AiOptions_Bedrock_FailsBecauseNoDirectProviderExists()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions { Provider = CollectorProvider.Bedrock });

        Assert.Contains("Ai:Provider", Failures(result), StringComparison.Ordinal);
    }
}
