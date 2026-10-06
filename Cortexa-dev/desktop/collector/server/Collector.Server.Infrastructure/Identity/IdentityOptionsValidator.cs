using Collector.Server.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Identity;

public sealed class IdentityOptionsValidator : IValidateOptions<IdentityOptions>
{
    private const int MinimumSigningKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, IdentityOptions options)
    {
        var failures = new OptionFailures();
        failures.RequireText(options.Issuer, Path(nameof(IdentityOptions.Issuer)));
        failures.RequireText(options.Audience, Path(nameof(IdentityOptions.Audience)));
        failures.RequireMinBytes(options.SigningKey, MinimumSigningKeyBytes, Path(nameof(IdentityOptions.SigningKey)));
        failures.RequireAbsoluteUri(options.BaseUrl, Path(nameof(IdentityOptions.BaseUrl)));
        failures.RequireText(options.InternalKey, Path(nameof(IdentityOptions.InternalKey)));
        failures.RequireText(options.InternalKeyHeaderName, Path(nameof(IdentityOptions.InternalKeyHeaderName)));
        failures.RequireNonNegative(options.StatusCacheSeconds, Path(nameof(IdentityOptions.StatusCacheSeconds)));
        failures.RequireNonNegative(options.ClockSkewSeconds, Path(nameof(IdentityOptions.ClockSkewSeconds)));
        failures.RequirePositive(options.StatusTimeoutSeconds, Path(nameof(IdentityOptions.StatusTimeoutSeconds)));
        return failures.ToResult();
    }

    private static string Path(string name) => $"{IdentityOptions.SectionName}:{name}";
}
