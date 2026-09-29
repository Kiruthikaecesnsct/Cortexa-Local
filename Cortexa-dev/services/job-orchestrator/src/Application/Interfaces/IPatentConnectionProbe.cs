using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public sealed record PatentSecretMaterial(string? ApiKey, string? ConsumerKey, string? OauthSecret);

public sealed record PatentProbeResult(bool Success, string? FailureReason)
{
    public static PatentProbeResult Succeeded() => new(true, null);

    public static PatentProbeResult Failed(string reason) => new(false, reason);
}

// A probe result never carries the credential it tested — success/failure and a
// coarse, non-identifying failure reason only. Implementations must never log or
// otherwise surface the credential value.
public interface IPatentConnectionProbe
{
    Task<PatentProbeResult> TestStoredAsync(PatentSource source, CancellationToken ct);

    Task<PatentProbeResult> TestSuppliedAsync(PatentSource source, PatentSecretMaterial credential, CancellationToken ct);
}
