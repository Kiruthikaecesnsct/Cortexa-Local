using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class AiProviderOptionsValidatorTests
{
    private readonly AiProviderOptionsValidator _validator = new();

    private static string Failures(Microsoft.Extensions.Options.ValidateOptionsResult result) =>
        string.Join('\n', result.Failures ?? []);

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        var result = _validator.Validate(null, new AiProviderOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("MEDIUM")]
    [InlineData("high")]
    public void Validate_KnownEffort_Succeeds(string effort)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Effort = effort });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankModel_Fails(string model)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Model = model });

        Assert.Contains("Model", Failures(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveMaxOutputTokens_Fails(int tokens)
    {
        var result = _validator.Validate(null, new AiProviderOptions { MaxOutputTokens = tokens });

        Assert.Contains("MaxOutputTokens", Failures(result));
    }

    [Theory]
    [InlineData("extreme")]
    [InlineData("")]
    public void Validate_UnknownEffort_Fails(string effort)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Effort = effort });

        Assert.Contains("Effort", Failures(result));
    }

    [Fact]
    public void Validate_UnknownThinkingMode_Fails()
    {
        var result = _validator.Validate(null, new AiProviderOptions { Thinking = "always" });

        Assert.Contains("Thinking", Failures(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData(AiProviderOptions.ThinkingAdaptive)]
    [InlineData(AiProviderOptions.ThinkingBetweenTools)]
    [InlineData(AiProviderOptions.ThinkingDisabled)]
    public void Validate_KnownThinkingMode_Succeeds(string mode)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Thinking = mode });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Validate_NonPositiveConcurrency_Fails(int concurrency)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Concurrency = concurrency });

        Assert.Contains("Concurrency", Failures(result));
    }

    [Fact]
    public void Validate_NegativeMaxRetries_Fails()
    {
        var result = _validator.Validate(null, new AiProviderOptions { MaxRetries = -1 });

        Assert.Contains("MaxRetries", Failures(result));
    }

    [Fact]
    public void Validate_ZeroMaxRetries_Succeeds()
    {
        var result = _validator.Validate(null, new AiProviderOptions { MaxRetries = 0 });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Validate_NonPositiveTimeout_Fails(int seconds)
    {
        var result = _validator.Validate(null, new AiProviderOptions { TimeoutSeconds = seconds });

        Assert.Contains("TimeoutSeconds", Failures(result));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void Validate_TemperatureOutsideZeroToOne_Fails(double temperature)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Temperature = temperature });

        Assert.Contains("Temperature", Failures(result));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void Validate_TemperatureWithinRange_Succeeds(double temperature)
    {
        var result = _validator.Validate(null, new AiProviderOptions { Temperature = temperature });

        Assert.True(result.Succeeded);
    }
}
