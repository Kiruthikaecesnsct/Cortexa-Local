namespace Collector.Application.Auth;

public interface ISessionState
{
    SessionState Current { get; }

    string? UserEmail { get; }

    DateTimeOffset? ExpiresAt { get; }

    event EventHandler? Changed;
}
