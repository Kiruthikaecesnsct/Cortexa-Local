using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class GitHubSignInService(
    DeviceFlowRunner runner,
    IOAuthTokenVault vault,
    IRemoteAuthState state,
    RemoteAuthAvailability availability) : IGitHubSignIn
{
    private static readonly RemoteAuthMethod[] OAuthMethods = [RemoteAuthMethod.GitHubApp, RemoteAuthMethod.GitHubDevice];

    public RemoteAuthMethod ActiveMethod => state.MethodFor(SourceType.Github);

    public bool IsSignedIn => ActiveMethod != RemoteAuthMethod.Pat;

    public Task<DeviceCodeGrant> StartAsync(RemoteAuthMethod method, CancellationToken cancellationToken)
    {
        if (!IsEnabled(method))
        {
            throw new InvalidOperationException($"The {method} sign-in is not enabled.");
        }

        return runner.RequestCodeAsync(method, cancellationToken);
    }

    public async Task<DeviceFlowOutcome> CompleteAsync(
        RemoteAuthMethod method,
        DeviceCodeGrant grant,
        CancellationToken cancellationToken)
    {
        var run = await runner.RunAsync(method, grant, cancellationToken);
        if (run.Tokens is null)
        {
            return run.Outcome;
        }

        return cancellationToken.IsCancellationRequested
            ? new DeviceFlowOutcome.Cancelled()
            : await PersistAsync(method, run.Tokens);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await vault.DeleteProviderAsync(SourceType.Github, cancellationToken);
        }
        finally
        {
            state.Reset(SourceType.Github);
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        foreach (var method in OAuthMethods)
        {
            if (await vault.ReadAsync(method, cancellationToken) is not null)
            {
                state.Set(SourceType.Github, method);
                return;
            }
        }
    }

    private bool IsEnabled(RemoteAuthMethod method) => method switch
    {
        RemoteAuthMethod.GitHubDevice => availability.DeviceEnabled,
        RemoteAuthMethod.GitHubApp => availability.AppEnabled,
        _ => false,
    };

    private async Task<DeviceFlowOutcome> PersistAsync(RemoteAuthMethod method, OAuthTokenSet tokens)
    {
        try
        {
            await vault.SaveAsync(method, tokens, CancellationToken.None);
            await vault.DeleteAsync(Other(method), CancellationToken.None);
        }
        catch (SecretStoreException)
        {
            return new DeviceFlowOutcome.Failed(DeviceFlowFailure.Other);
        }

        state.Set(SourceType.Github, method);
        return new DeviceFlowOutcome.SignedIn();
    }

    private static RemoteAuthMethod Other(RemoteAuthMethod method) =>
        method == RemoteAuthMethod.GitHubApp ? RemoteAuthMethod.GitHubDevice : RemoteAuthMethod.GitHubApp;
}
