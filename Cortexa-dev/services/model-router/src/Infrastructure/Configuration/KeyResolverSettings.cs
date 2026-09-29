namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public sealed class KeyResolverSettings
{
    public int SecretCacheTtlSeconds { get; set; } = 3600;
}
