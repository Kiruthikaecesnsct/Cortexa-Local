using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Remote;

public class RemoteSourceOptionsValidatorTests
{
    private readonly RemoteSourceOptionsValidator _validator = new();

    private ValidateOptionsResult Validate(Action<RemoteSourceOptions>? change = null)
    {
        var options = new RemoteSourceOptions();
        change?.Invoke(options);
        return _validator.Validate(null, options);
    }

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        Assert.True(Validate().Succeeded);
    }

    [Fact]
    public void Validate_ZeroMaxRetries_Succeeds()
    {
        Assert.True(Validate(options => options.RateLimit.MaxRetries = 0).Succeeded);
    }

    [Fact]
    public void Validate_ZeroMinRemaining_Succeeds()
    {
        Assert.True(Validate(options => options.GitHub.MinRemaining = 0).Succeeded);
    }

    public static TheoryData<string, Action<RemoteSourceOptions>> InvalidChanges => new()
    {
        { "GitHub:BaseUrl", options => options.GitHub.BaseUrl = "http://api.github.com" },
        { "GitHub:BaseUrl", options => options.GitHub.BaseUrl = "not a url" },
        { "GitHub:BaseUrl", options => options.GitHub.BaseUrl = string.Empty },
        { "AzureDevOps:BaseUrl", options => options.AzureDevOps.BaseUrl = "ftp://dev.azure.com" },
        { "GitHub:ApiVersion", options => options.GitHub.ApiVersion = " " },
        { "AzureDevOps:ApiVersion", options => options.AzureDevOps.ApiVersion = string.Empty },
        { "MinRemaining", options => options.GitHub.MinRemaining = -1 },
        { "MinRemaining", options => options.AzureDevOps.MinRemaining = -1 },
        { "TimeoutSeconds", options => options.TimeoutSeconds = 0 },
        { "MaxPages", options => options.MaxPages = 0 },
        { "MaxPauseSeconds", options => options.RateLimit.MaxPauseSeconds = 0 },
        { "SecondaryWaitSeconds", options => options.RateLimit.SecondaryWaitSeconds = -5 },
        { "MaxConcurrency", options => options.RateLimit.MaxConcurrency = 0 },
        { "MaxRetries", options => options.RateLimit.MaxRetries = -1 },
        { "CacheRoot", options => options.CacheRoot = " " },
    };

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public void Validate_InvalidSetting_FailsAndNamesTheSetting(string expectedName, Action<RemoteSourceOptions> change)
    {
        var result = Validate(change);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(expectedName, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MultipleInvalidSettings_ReportsEachFailure()
    {
        var result = Validate(options =>
        {
            options.TimeoutSeconds = 0;
            options.MaxPages = 0;
            options.CacheRoot = string.Empty;
        });

        Assert.Equal(3, result.Failures!.Count());
    }
}
