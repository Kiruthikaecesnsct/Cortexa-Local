namespace Cortexa.JobOrchestrator.Application.Interfaces;

// Deliberately write/exists-only: this contract never returns secret material,
// so nothing above this layer can accidentally echo a patent API key.
public interface IPatentSecretWriter
{
    Task SetSecretAsync(string secretName, string value, CancellationToken ct);

    Task<bool> SecretExistsAsync(string secretName, CancellationToken ct);
}
