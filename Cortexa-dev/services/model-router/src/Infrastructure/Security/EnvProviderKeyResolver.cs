using Microsoft.Extensions.Configuration;

namespace Cortexa.ModelRouter.Infrastructure.Security;

public sealed class EnvProviderKeyResolver : IProviderKeyResolver
{
    private readonly IConfiguration _configuration;

    public EnvProviderKeyResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<string> ResolveAsync(string secretName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new InvalidOperationException("Provider key secret name must not be empty.");

        var envVarName = ToEnvVarName(secretName);
        var value = _configuration[$"Secrets:{secretName}"]
            ?? Environment.GetEnvironmentVariable(envVarName)
            ?? _configuration[envVarName];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"No API key configured for '{secretName}'. Set environment variable '{envVarName}' (e.g. in docker-compose .env) or configuration key 'Secrets:{secretName}'.");
        }

        return Task.FromResult(value);
    }

    private static string ToEnvVarName(string secretName) =>
        secretName.Replace('-', '_').ToUpperInvariant();
}
