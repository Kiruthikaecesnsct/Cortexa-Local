using Collector.Application.Ports;
using Collector.Application.Secrets;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Auth;

public sealed class SessionService : ISignInService, IAccessTokenProvider, ISessionState
{
    private readonly IAuthClient _client;
    private readonly ISecretStore _secrets;
    private readonly TokenRefreshPolicy _policy;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _stateLock = new();
    private SessionState _current = SessionState.SignedOut;
    private string? _email;
    private string? _accessToken;
    private DateTimeOffset? _expiresAt;

    public SessionService(
        IAuthClient client,
        ISecretStore secrets,
        TokenRefreshPolicy policy,
        TimeProvider time,
        ILogger<SessionService> logger)
    {
        _client = client;
        _secrets = secrets;
        _policy = policy;
        _time = time;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public SessionState Current
    {
        get { lock (_stateLock) { return _current; } }
    }

    public string? UserEmail
    {
        get { lock (_stateLock) { return _email; } }
    }

    public DateTimeOffset? ExpiresAt
    {
        get { lock (_stateLock) { return _expiresAt; } }
    }

    public async Task<SignInOutcome> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var invalid = SignInRules.Validate(email, password);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await _client.LoginAsync(email.Trim(), password, cancellationToken);
        if (result.Tokens is null)
        {
            return SignInOutcomeMapper.FromFailure(result.Failure!);
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await PersistAsync(result.Tokens, cancellationToken);
            SetState(SessionState.SignedIn, email.Trim(), result.Tokens);
        }
        finally
        {
            _refreshGate.Release();
        }

        _logger.LogInformation("Signed in.");
        return new SignInOutcome.Success();
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await ClearSlotsAsync(cancellationToken);
            SetCleared(SessionState.SignedOut);
        }
        finally
        {
            _refreshGate.Release();
        }

        _logger.LogInformation("Signed out.");
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var access = await _secrets.ReadAsync(SecretSlot.CortexaAccessToken, cancellationToken);
        var refresh = await _secrets.ReadAsync(SecretSlot.CortexaRefreshToken, cancellationToken);
        if (access is null || refresh is null)
        {
            SetCleared(SessionState.SignedOut);
            return;
        }

        var expiresAt = AccessTokenClaims.ReadExpiry(access);
        if (expiresAt is not null && !_policy.IsRefreshDue(expiresAt.Value, _time.GetUtcNow()))
        {
            SetState(SessionState.SignedIn, AccessTokenClaims.ReadEmail(access), access, expiresAt.Value);
            return;
        }

        await RestoreByRefreshAsync(access, cancellationToken);
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            return Task.FromResult(_current == SessionState.SignedIn ? _accessToken : null);
        }
    }

    public async Task<string?> RefreshAfterUnauthorizedAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        var attempt = await RefreshSingleFlightAsync(rejectedAccessToken, cancellationToken);
        if (attempt.Status == RefreshStatus.Rejected)
        {
            SetCleared(SessionState.Expired);
        }

        return attempt.AccessToken;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        string? seen;
        lock (_stateLock)
        {
            seen = _accessToken;
        }

        if (seen is null)
        {
            return false;
        }

        var attempt = await RefreshSingleFlightAsync(seen, cancellationToken);
        if (attempt.Status == RefreshStatus.Rejected)
        {
            SetCleared(SessionState.Expired);
        }

        return attempt.AccessToken is not null;
    }

    public async Task MarkExpiredAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await ClearSlotsAsync(cancellationToken);
            SetCleared(SessionState.Expired);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RestoreByRefreshAsync(string access, CancellationToken cancellationToken)
    {
        var attempt = await RefreshSingleFlightAsync(access, cancellationToken);
        if (attempt.AccessToken is null)
        {
            SetCleared(SessionState.SignedOut);
        }
    }

    private async Task<RefreshAttempt> RefreshSingleFlightAsync(string rejected, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                if (_accessToken is not null && _accessToken != rejected && _current == SessionState.SignedIn)
                {
                    return new RefreshAttempt(RefreshStatus.Refreshed, _accessToken);
                }
            }

            return await RefreshCoreAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<RefreshAttempt> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _secrets.ReadAsync(SecretSlot.CortexaRefreshToken, cancellationToken);
        if (refreshToken is null)
        {
            return new RefreshAttempt(RefreshStatus.Rejected, null);
        }

        var result = await _client.RefreshAsync(refreshToken, cancellationToken);
        if (result.Tokens is not null)
        {
            await PersistAsync(result.Tokens, cancellationToken);
            SetState(SessionState.SignedIn, UserEmail ?? AccessTokenClaims.ReadEmail(result.Tokens.AccessToken), result.Tokens);
            return new RefreshAttempt(RefreshStatus.Refreshed, result.Tokens.AccessToken);
        }

        return await HandleRefreshFailureAsync(result.Failure!, cancellationToken);
    }

    private async Task<RefreshAttempt> HandleRefreshFailureAsync(AuthFailure failure, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Token refresh failed: {Kind}.", failure.Kind);
        if (failure.Kind != AuthFailureKind.InvalidRefreshToken)
        {
            return new RefreshAttempt(RefreshStatus.Failed, null);
        }

        await ClearSlotsAsync(cancellationToken);
        return new RefreshAttempt(RefreshStatus.Rejected, null);
    }

    private async Task PersistAsync(AuthTokens tokens, CancellationToken cancellationToken)
    {
        await _secrets.WriteAsync(SecretSlot.CortexaRefreshToken, tokens.RefreshToken, cancellationToken);
        await _secrets.WriteAsync(SecretSlot.CortexaAccessToken, tokens.AccessToken, cancellationToken);
    }

    private async Task ClearSlotsAsync(CancellationToken cancellationToken)
    {
        await _secrets.DeleteAsync(SecretSlot.CortexaAccessToken, cancellationToken);
        await _secrets.DeleteAsync(SecretSlot.CortexaRefreshToken, cancellationToken);
    }

    private void SetState(SessionState state, string? email, AuthTokens tokens) =>
        SetState(state, email, tokens.AccessToken, tokens.ExpiresAt);

    private void SetState(SessionState state, string? email, string accessToken, DateTimeOffset expiresAt)
    {
        lock (_stateLock)
        {
            _current = state;
            _email = email;
            _accessToken = accessToken;
            _expiresAt = expiresAt;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetCleared(SessionState state)
    {
        lock (_stateLock)
        {
            _current = state;
            _accessToken = null;
            _expiresAt = null;
            if (state == SessionState.SignedOut)
            {
                _email = null;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private enum RefreshStatus
    {
        Refreshed,
        Rejected,
        Failed,
    }

    private sealed record RefreshAttempt(RefreshStatus Status, string? AccessToken);
}
