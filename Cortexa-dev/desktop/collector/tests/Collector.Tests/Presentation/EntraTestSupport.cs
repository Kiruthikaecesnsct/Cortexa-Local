using Collector.Application.Remote.Auth;

namespace Collector.Tests.Presentation;

internal sealed class FakeEntraSignIn : IEntraIdSignIn
{
    public bool IsSignedIn { get; set; }

    public EntraSignInOutcome Outcome { get; set; } = new EntraSignInOutcome.SignedIn("access");

    public TaskCompletionSource<EntraSignInOutcome>? Gate { get; set; }

    public EntraDeviceCode? Code { get; set; }

    public Exception? SignInError { get; set; }

    public Exception? SignOutError { get; set; }

    public int InteractiveCalls { get; private set; }

    public int CodeCalls { get; private set; }

    public int SignOutCalls { get; private set; }

    public Task<EntraSignInOutcome> SignInAsync(CancellationToken cancellationToken)
    {
        InteractiveCalls++;
        return CompleteAsync(cancellationToken);
    }

    public async Task<EntraSignInOutcome> SignInWithCodeAsync(
        Func<EntraDeviceCode, Task> onCode,
        CancellationToken cancellationToken)
    {
        CodeCalls++;
        if (Code is not null)
        {
            await onCode(Code);
        }

        return await CompleteAsync(cancellationToken);
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        SignOutCalls++;
        if (SignOutError is not null)
        {
            return Task.FromException(SignOutError);
        }

        IsSignedIn = false;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<EntraSignInOutcome> CompleteAsync(CancellationToken cancellationToken)
    {
        if (SignInError is not null)
        {
            throw SignInError;
        }

        var outcome = Gate is null ? Outcome : await Gate.Task.WaitAsync(cancellationToken);
        IsSignedIn = outcome is EntraSignInOutcome.SignedIn;
        return outcome;
    }
}
