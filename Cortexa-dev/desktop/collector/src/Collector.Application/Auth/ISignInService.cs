namespace Collector.Application.Auth;

public interface ISignInService
{
    Task<SignInOutcome> SignInAsync(string email, string password, CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task RestoreAsync(CancellationToken cancellationToken);
}
