using Collector.Application.Auth;
using Collector.Application.Settings;
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
