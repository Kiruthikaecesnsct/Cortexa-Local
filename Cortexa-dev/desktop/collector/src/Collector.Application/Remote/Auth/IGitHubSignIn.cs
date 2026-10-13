namespace Collector.Application.Remote.Auth;

public interface IGitHubSignIn
{
    RemoteAuthMethod ActiveMethod { get; }

    bool IsSignedIn { get; }

    Task<DeviceCodeGrant> StartAsync(RemoteAuthMethod method, CancellationToken cancellationToken);

    Task<DeviceFlowOutcome> CompleteAsync(
        RemoteAuthMethod method,
        DeviceCodeGrant grant,
        CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task RestoreAsync(CancellationToken cancellationToken);
}
