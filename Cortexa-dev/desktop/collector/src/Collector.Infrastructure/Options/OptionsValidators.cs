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
    public ValidateOptionsResult Validate(string? name, CollectorServerOptions options)
    {
        var failures = new List<string>();
        var url = OptionsChecks.Url(CollectorServerOptions.SectionName, options.BaseUrl);
        if (url.Failed)
        {
            failures.AddRange(url.Failures ?? []);
        }

        if (options.ReadTimeoutSeconds <= 0)
        {
            failures.Add($"{CollectorServerOptions.SectionName}:{nameof(options.ReadTimeoutSeconds)} must be greater than zero.");
        }

        if (options.ReadRetryDelaysMs.Any(delay => delay < 0))
        {
            failures.Add($"{CollectorServerOptions.SectionName}:{nameof(options.ReadRetryDelaysMs)} must not contain negative values.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class HistoryOptionsValidator : IValidateOptions<HistoryOptions>
{
    public ValidateOptionsResult Validate(string? name, HistoryOptions options)
    {
        var failures = new List<string>();
        if (options.PollSeconds <= 0)
        {
            failures.Add($"{HistoryOptions.SectionName}:{nameof(options.PollSeconds)} must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.TextEditorPath))
        {
            failures.Add($"{HistoryOptions.SectionName}:{nameof(options.TextEditorPath)} is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
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

public sealed class BedrockProviderOptionsValidator : IValidateOptions<BedrockProviderOptions>
{
    private static readonly string[] InferenceProfilePrefixes = ["us.", "eu.", "apac."];

    public ValidateOptionsResult Validate(string? name, BedrockProviderOptions options)
    {
        var failures = new List<string>();
        AddIf(failures, string.IsNullOrWhiteSpace(options.SsoStartUrl), nameof(options.SsoStartUrl), "is required");
        AddIf(failures, string.IsNullOrWhiteSpace(options.SsoRegion), nameof(options.SsoRegion), "is required");
        AddIf(failures, string.IsNullOrWhiteSpace(options.AccountId), nameof(options.AccountId), "is required");
        AddIf(failures, string.IsNullOrWhiteSpace(options.SsoRoleName), nameof(options.SsoRoleName), "is required");
        AddIf(failures, string.IsNullOrWhiteSpace(options.Region), nameof(options.Region), "is required");
        AddModelFailure(failures, options.Model);
        AddIf(failures, options.MaxOutputTokens <= 0, nameof(options.MaxOutputTokens), "must be greater than zero");
        AddIf(failures, options.Concurrency <= 0, nameof(options.Concurrency), "must be greater than zero");
        AddIf(failures, options.MaxRetries < 0, nameof(options.MaxRetries), "must not be negative");
        AddIf(failures, options.TimeoutSeconds <= 0, nameof(options.TimeoutSeconds), "must be greater than zero");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddModelFailure(List<string> failures, string model)
    {
        AddIf(failures, string.IsNullOrWhiteSpace(model), nameof(BedrockProviderOptions.Model), "is required");
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        AddIf(failures, model.Any(char.IsWhiteSpace), nameof(BedrockProviderOptions.Model), "must not contain whitespace");
        AddIf(failures, !IsInferenceProfileId(model), nameof(BedrockProviderOptions.Model), "must be a cross-region inference-profile id (e.g. us.anthropic.*)");
    }

    private static bool IsInferenceProfileId(string model) =>
        InferenceProfilePrefixes.Any(prefix => model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static void AddIf(List<string> failures, bool failed, string name, string reason)
    {
        if (failed)
        {
            failures.Add($"{BedrockProviderOptions.SectionName}:{name} {reason}.");
        }
    }
}

public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public static readonly CollectorProvider[] SupportedProviders =
        [CollectorProvider.Claude, CollectorProvider.Gemini, CollectorProvider.Bedrock];

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

public sealed class RemoteSourceOptionsValidator : IValidateOptions<RemoteSourceOptions>
{
    public ValidateOptionsResult Validate(string? name, RemoteSourceOptions options)
    {
        var failures = new List<string>();
        CheckProvider(failures, "GitHub", options.GitHub);
        CheckProvider(failures, "AzureDevOps", options.AzureDevOps);
        CheckSsh(failures, options.Ssh);
        CheckPositive(failures, nameof(options.TimeoutSeconds), options.TimeoutSeconds);
        CheckPositive(failures, nameof(options.MaxPages), options.MaxPages);
        CheckPositive(failures, $"RateLimit:{nameof(options.RateLimit.MaxPauseSeconds)}", options.RateLimit.MaxPauseSeconds);
        CheckPositive(failures, $"RateLimit:{nameof(options.RateLimit.SecondaryWaitSeconds)}", options.RateLimit.SecondaryWaitSeconds);
        CheckPositive(failures, $"RateLimit:{nameof(options.RateLimit.MaxConcurrency)}", options.RateLimit.MaxConcurrency);
        CheckNotNegative(failures, $"RateLimit:{nameof(options.RateLimit.MaxRetries)}", options.RateLimit.MaxRetries);
        if (string.IsNullOrWhiteSpace(options.CacheRoot))
        {
            failures.Add($"{RemoteSourceOptions.SectionName}:{nameof(options.CacheRoot)} is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckSsh(List<string> failures, SshSourceOptions options)
    {
        const string Prefix = $"{RemoteSourceOptions.SectionName}:Ssh";
        if (options.DefaultPort is <= 0 or > 65535)
        {
            failures.Add($"{Prefix}:{nameof(options.DefaultPort)} must be between 1 and 65535.");
        }

        CheckPositive(failures, $"Ssh:{nameof(options.ConnectTimeoutSeconds)}", options.ConnectTimeoutSeconds);
        CheckPositive(failures, $"Ssh:{nameof(options.MaxWalkDepth)}", options.MaxWalkDepth);
    }

    private static void CheckProvider(List<string> failures, string provider, RemoteProviderOptions options)
    {
        var prefix = $"{RemoteSourceOptions.SectionName}:{provider}";
        var validUrl = Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        if (!validUrl)
        {
            failures.Add($"{prefix}:{nameof(options.BaseUrl)} must be an absolute https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            failures.Add($"{prefix}:{nameof(options.ApiVersion)} is required.");
        }

        CheckNotNegative(failures, $"{provider}:{nameof(options.MinRemaining)}", options.MinRemaining);
    }

    private static void CheckPositive(List<string> failures, string name, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{RemoteSourceOptions.SectionName}:{name} must be greater than zero.");
        }
    }

    private static void CheckNotNegative(List<string> failures, string name, int value)
    {
        if (value < 0)
        {
            failures.Add($"{RemoteSourceOptions.SectionName}:{name} must not be negative.");
        }
    }
}
