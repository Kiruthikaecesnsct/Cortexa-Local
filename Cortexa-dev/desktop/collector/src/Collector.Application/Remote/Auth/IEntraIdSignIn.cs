namespace Collector.Application.Remote.Auth;

public interface IEntraIdSignIn
{
    bool IsSignedIn { get; }

    Task<EntraSignInOutcome> SignInAsync(CancellationToken cancellationToken);

    Task<EntraSignInOutcome> SignInWithCodeAsync(
        Func<EntraDeviceCode, Task> onCode,
        CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task RestoreAsync(CancellationToken cancellationToken);
}
