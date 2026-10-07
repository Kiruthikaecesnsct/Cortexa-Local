using Collector.Application.Auth;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Options;

public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options) =>
        OptionsChecks.Url(GatewayOptions.SectionName, options.BaseUrl);
}

public sealed class CollectorServerOptionsValidator : IValidateOptions<CollectorServerOptions>
{
    public ValidateOptionsResult Validate(string? name, CollectorServerOptions options) =>
        OptionsChecks.Url(CollectorServerOptions.SectionName, options.BaseUrl);
}

public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    public ValidateOptionsResult Validate(string? name, CacheOptions options) =>
        OptionsChecks.Required($"{CacheOptions.SectionName}:DatabasePath", options.DatabasePath);
}

public sealed class SecretsOptionsValidator : IValidateOptions<SecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretsOptions options) =>
        OptionsChecks.Required($"{SecretsOptions.SectionName}:TargetPrefix", options.TargetPrefix);
}

public sealed class UserSettingsOptionsValidator : IValidateOptions<UserSettingsOptions>
{
    public ValidateOptionsResult Validate(string? name, UserSettingsOptions options) =>
        OptionsChecks.Required($"{UserSettingsOptions.SectionName}:Path", options.Path);
}

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();
        AddIfNotPositive(failures, nameof(options.RefreshSkewSeconds), options.RefreshSkewSeconds);
        AddIfNotPositive(failures, nameof(options.MinRefreshDelaySeconds), options.MinRefreshDelaySeconds);
        AddIfNotPositive(failures, nameof(options.HttpTimeoutSeconds), options.HttpTimeoutSeconds);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddIfNotPositive(List<string> failures, string name, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{AuthOptions.SectionName}:{name} must be greater than zero.");
        }
    }
}

public sealed class AiProviderOptionsValidator : IValidateOptions<AiProviderOptions>
{
    private const double MinTemperature = 0.0;
    private const double MaxTemperature = 1.0;

    private static readonly string[] ThinkingModes =
    [
        string.Empty,
        AiProviderOptions.ThinkingBetweenTools,
        AiProviderOptions.ThinkingAdaptive,
        AiProviderOptions.ThinkingDisabled,
    ];

    public ValidateOptionsResult Validate(string? name, AiProviderOptions options)
    {
        var failures = new List<string>();
        AddIf(failures, string.IsNullOrWhiteSpace(options.Model), nameof(options.Model), "is required");
        AddIf(failures, options.MaxOutputTokens <= 0, nameof(options.MaxOutputTokens), "must be greater than zero");
        AddIf(failures, options.Concurrency <= 0, nameof(options.Concurrency), "must be greater than zero");
        AddIf(failures, options.MaxRetries < 0, nameof(options.MaxRetries), "must not be negative");
        AddIf(failures, options.TimeoutSeconds <= 0, nameof(options.TimeoutSeconds), "must be greater than zero");
        AddIf(failures, !Enum.TryParse<Anthropic.Models.Messages.Effort>(options.Effort, true, out _), nameof(options.Effort), "is not a known effort level");
        AddIf(failures, !ThinkingModes.Contains(options.Thinking), nameof(options.Thinking), "is not a known thinking mode");
        AddIf(failures, options.Temperature is < MinTemperature or > MaxTemperature, nameof(options.Temperature), "must be between 0 and 1");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddIf(List<string> failures, bool failed, string name, string reason)
    {
        if (failed)
        {
            failures.Add($"{AiProviderOptions.SectionName}:{name} {reason}.");
        }
    }
}

public sealed class GeminiProviderOptionsValidator : IValidateOptions<GeminiProviderOptions>
{
    private const double MinTemperature = 0.0;
    private const double MaxTemperature = 2.0;

    private static readonly string[] ThinkingLevels =
    [
        string.Empty,
        GeminiProviderOptions.ThinkingLow,
        GeminiProviderOptions.ThinkingMedium,
        GeminiProviderOptions.ThinkingHigh,
    ];

    public ValidateOptionsResult Validate(string? name, GeminiProviderOptions options)
    {
        var failures = new List<string>();
        AddIf(failures, string.IsNullOrWhiteSpace(options.Model), nameof(options.Model), "is required");
        AddIf(failures, options.MaxOutputTokens <= 0, nameof(options.MaxOutputTokens), "must be greater than zero");
        AddIf(failures, options.Concurrency <= 0, nameof(options.Concurrency), "must be greater than zero");
        AddIf(failures, options.MaxRetries < 0, nameof(options.MaxRetries), "must not be negative");
        AddIf(failures, options.TimeoutSeconds <= 0, nameof(options.TimeoutSeconds), "must be greater than zero");
        AddIf(failures, !ThinkingLevels.Contains(options.Thinking), nameof(options.Thinking), "is not a known thinking level");
        AddIf(failures, options.Temperature is < MinTemperature or > MaxTemperature, nameof(options.Temperature), "must be between 0 and 2");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddIf(List<string> failures, bool failed, string name, string reason)
    {
        if (failed)
        {
            failures.Add($"{GeminiProviderOptions.SectionName}:{name} {reason}.");
        }
    }
}

public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public static readonly CollectorProvider[] SupportedProviders = [CollectorProvider.Claude, CollectorProvider.Gemini];

    public ValidateOptionsResult Validate(string? name, AiOptions options) =>
        SupportedProviders.Contains(options.Provider)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{AiOptions.SectionName}:Provider must be one of: {string.Join(", ", SupportedProviders)}.");
}

internal static class OptionsChecks
{
    public static ValidateOptionsResult Url(string section, string? value)
    {
        var reason = EndpointSettingsRules.ValidateUrl(value);
        return reason is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{section}:BaseUrl is invalid. {reason}");
    }

    public static ValidateOptionsResult Required(string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ValidateOptionsResult.Fail($"{key} is required.")
            : ValidateOptionsResult.Success;
}
