using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class EntraIdSignInService(IEntraIdAuthClient client, IRemoteAuthState state) : IEntraIdSignIn
{
    public bool IsSignedIn => state.MethodFor(SourceType.AzureDevops) == RemoteAuthMethod.EntraId;

    public async Task<EntraSignInOutcome> SignInAsync(CancellationToken cancellationToken) =>
        Apply(await client.AcquireInteractiveAsync(cancellationToken));

    public async Task<EntraSignInOutcome> SignInWithCodeAsync(
        Func<EntraDeviceCode, Task> onCode,
        CancellationToken cancellationToken) =>
        Apply(await client.AcquireWithDeviceCodeAsync(onCode, cancellationToken));

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.SignOutAsync(cancellationToken);
        }
        finally
        {
            state.Reset(SourceType.AzureDevops);
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (await client.HasAccountAsync(cancellationToken))
        {
            state.Set(SourceType.AzureDevops, RemoteAuthMethod.EntraId);
        }
    }

    private EntraSignInOutcome Apply(EntraSignInOutcome outcome)
    {
        if (outcome is EntraSignInOutcome.SignedIn)
        {
            state.Set(SourceType.AzureDevops, RemoteAuthMethod.EntraId);
        }

        return outcome;
    }
}
