using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Configuration;

namespace Collector.Tests.Ai;

public sealed class GeminiProviderOptionsValidatorTests
{
    private const string CatalogFallback = "gemini-3.6-flash";
    private const string UnknownModel = "gemini-9.9-unknown";

    private readonly GeminiProviderOptionsValidator _validator = new(Microsoft.Extensions.Options.Options.Create(new ProviderModelCatalog()));

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
            MaxInputTokens = 0,
            MaxOutputTokens = 0,
            Concurrency = 0,
            MaxRetries = -1,
            TimeoutSeconds = 0,
            Temperature = 2.5,
        };

        var failures = Failures(_validator.Validate(null, options));

        foreach (var field in new[] { "Model", "MaxInputTokens", "MaxOutputTokens", "Concurrency", "MaxRetries", "TimeoutSeconds", "Temperature" })
        {
            Assert.Contains($"Ai:Gemini:{field}", failures, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveMaxInputTokens_Fails(int value)
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { MaxInputTokens = value });

        Assert.Contains("Ai:Gemini:MaxInputTokens", Failures(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DefaultMaxInputTokens_IsNamedConstant()
    {
        Assert.Equal(GeminiProviderOptions.DefaultMaxInputTokens, new GeminiProviderOptions().MaxInputTokens);
    }

    [Fact]
    public void Validate_EmptyFallbackModels_Succeeds()
    {
        Assert.True(_validator.Validate(null, new GeminiProviderOptions { FallbackModels = [] }).Succeeded);
    }

    [Fact]
    public void Validate_FallbackModelInCatalog_Succeeds()
    {
        Assert.True(_validator.Validate(null, new GeminiProviderOptions { FallbackModels = [CatalogFallback] }).Succeeded);
    }

    [Fact]
    public void Validate_FallbackRepeatsThePrimaryModel_Succeeds()
    {
        var options = new GeminiProviderOptions { FallbackModels = [new GeminiProviderOptions().Model] };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_UnknownFallbackModel_FailsNamingTheEntry()
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { FallbackModels = [UnknownModel] });

        Assert.Contains($"entry '{UnknownModel}'", Failures(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RepeatedFallbackModel_FailsNamingTheEntry()
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { FallbackModels = [CatalogFallback, CatalogFallback] });

        var failures = Failures(result);
        Assert.Contains($"entry '{CatalogFallback}' is repeated", failures, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_BlankFallbackModel_Fails(string model)
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { FallbackModels = [model] });

        Assert.Contains("is blank", Failures(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NegativeMaxQuotaRetryDelay_Fails()
    {
        var result = _validator.Validate(null, new GeminiProviderOptions { MaxQuotaRetryDelaySeconds = -1 });

        Assert.Contains("Ai:Gemini:MaxQuotaRetryDelaySeconds", Failures(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ZeroMaxQuotaRetryDelay_Succeeds()
    {
        Assert.True(_validator.Validate(null, new GeminiProviderOptions { MaxQuotaRetryDelaySeconds = 0 }).Succeeded);
    }

    [Fact]
    public void Bind_FallbackModelsFromConfiguration_YieldsExactlyTheConfiguredItems()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Gemini:FallbackModels:0"] = CatalogFallback,
                ["Ai:Gemini:FallbackModels:1"] = UnknownModel,
            })
            .Build();

        var options = configuration.GetSection(GeminiProviderOptions.SectionName).Get<GeminiProviderOptions>();

        Assert.Equal([CatalogFallback, UnknownModel], options!.FallbackModels);
    }

    [Theory]
    [InlineData(CollectorProvider.Claude)]
    [InlineData(CollectorProvider.Gemini)]
    [InlineData(CollectorProvider.Bedrock)]
    public void AiOptions_SupportedProvider_Succeeds(CollectorProvider provider)
    {
        Assert.True(new AiOptionsValidator().Validate(null, new AiOptions { Provider = provider }).Succeeded);
    }
}
