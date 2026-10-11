using Collector.Domain.Enums;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class GroqProviderOptionsValidatorTests
{
    private readonly GroqProviderOptionsValidator _validator = new();

    private static string Failures(Microsoft.Extensions.Options.ValidateOptionsResult result) =>
        string.Join('\n', result.Failures ?? []);

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        Assert.True(_validator.Validate(null, new GroqProviderOptions()).Succeeded);
    }

    [Fact]
    public void Validate_InvalidNumbersModelAndBaseUrl_ReportsEveryField()
    {
        var options = new GroqProviderOptions
        {
            Model = " ",
            BaseUrl = " ",
            MaxOutputTokens = 0,
            Concurrency = 0,
            MaxRetries = -1,
            TimeoutSeconds = 0,
            Temperature = 2.5,
        };

        var failures = Failures(_validator.Validate(null, options));

        foreach (var field in new[] { "Model", "BaseUrl", "MaxOutputTokens", "Concurrency", "MaxRetries", "TimeoutSeconds", "Temperature" })
        {
            Assert.Contains($"Ai:Groq:{field}", failures, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Validate_TemperatureWithinRange_Succeeds(double temperature)
    {
        Assert.True(_validator.Validate(null, new GroqProviderOptions { Temperature = temperature }).Succeeded);
    }

    [Fact]
    public void Validate_NegativeRetries_Fails()
    {
        var result = _validator.Validate(null, new GroqProviderOptions { MaxRetries = -1 });

        Assert.Contains("Ai:Groq:MaxRetries", Failures(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CollectorProvider.Claude)]
    [InlineData(CollectorProvider.Gemini)]
    [InlineData(CollectorProvider.Bedrock)]
    [InlineData(CollectorProvider.Groq)]
    public void AiOptions_SupportedProvider_Succeeds(CollectorProvider provider)
    {
        Assert.True(new AiOptionsValidator().Validate(null, new AiOptions { Provider = provider }).Succeeded);
    }
}
