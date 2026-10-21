using Collector.Application.Remote.Auth;

namespace Collector.Application.Ports;

public interface IEntraIdAuthClient
{
    Task<EntraSignInOutcome> AcquireSilentAsync(CancellationToken cancellationToken);

    Task<EntraSignInOutcome> AcquireInteractiveAsync(CancellationToken cancellationToken);

    Task<EntraSignInOutcome> AcquireWithDeviceCodeAsync(
        Func<EntraDeviceCode, Task> onCode,
        CancellationToken cancellationToken);

    Task<bool> HasAccountAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}
