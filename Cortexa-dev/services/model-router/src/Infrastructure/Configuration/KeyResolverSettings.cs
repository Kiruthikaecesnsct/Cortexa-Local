namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public sealed class KeyResolverSettings
{
    // Empty = auto-detect from KeyVault:Uri (empty -> Env, set -> KeyVault).
    // Set explicitly to "Env" or "KeyVault" to override the auto-detection.
    public string Provider { get; set; } = string.Empty;
    public int SecretCacheTtlSeconds { get; set; } = 3600;
}
