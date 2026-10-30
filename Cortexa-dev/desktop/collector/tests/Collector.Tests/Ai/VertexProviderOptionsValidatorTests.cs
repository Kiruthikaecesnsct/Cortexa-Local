using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Ai;

public sealed class VertexProviderOptionsValidatorTests
{
    private static VertexProviderOptionsValidator CreateValidator() =>
        new(Options.Create(new ProviderModelCatalog()));

    private static VertexProviderOptions Valid() => new()
    {
        ProjectId = "cortexa-test-project",
        Location = "us-central1",
        Model = "gemini-3.6-flash",
        MaxOutputTokens = 8192,
        Concurrency = 4,
        MaxRetries = 1,
        TimeoutSeconds = 120,
        TokenRefreshSkewSeconds = 300,
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = CreateValidator().Validate(null, Valid());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyProjectId_FailsNamingTheField()
    {
        var options = Valid();
        options.ProjectId = string.Empty;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.ProjectId), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyLocation_FailsNamingTheField()
    {
        var options = Valid();
        options.Location = "   ";

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.Location), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ModelNotInCatalog_FailsNamingTheField()
    {
        var options = Valid();
        options.Model = "not-a-catalog-model";

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.Model), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MaxOutputTokensZero_FailsNamingTheField()
    {
        var options = Valid();
        options.MaxOutputTokens = 0;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.MaxOutputTokens), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ConcurrencyNotPositive_Fails(int concurrency)
    {
        var options = Valid();
        options.Concurrency = concurrency;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.Concurrency), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TimeoutSecondsZero_Fails()
    {
        var options = Valid();
        options.TimeoutSeconds = 0;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.TimeoutSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TokenRefreshSkewSecondsZero_Fails()
    {
        var options = Valid();
        options.TokenRefreshSkewSeconds = 0;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(nameof(VertexProviderOptions.TokenRefreshSkewSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeMaxRetries_Fails()
    {
        var options = Valid();
        options.MaxRetries = -1;

        var result = CreateValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains(nameof(VertexProviderOptions.MaxRetries), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroMaxRetries_Succeeds()
    {
        var options = Valid();
        options.MaxRetries = 0;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
