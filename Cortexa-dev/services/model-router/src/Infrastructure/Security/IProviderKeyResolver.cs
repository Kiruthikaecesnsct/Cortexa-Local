namespace Cortexa.ModelRouter.Infrastructure.Security;

public interface IProviderKeyResolver
{
    Task<string> ResolveAsync(string secretName, CancellationToken ct = default);
}
