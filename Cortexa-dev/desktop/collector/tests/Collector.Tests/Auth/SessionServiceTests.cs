using Collector.Application.Auth;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Presentation;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Auth;

public class SessionServiceTests
{
    private const string Email = "user@example.com";
    private const string Password = "secret";
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeAuthClient _client = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly InMemorySessionCredentials _credentials = new();
    private readonly FakeSshCloser _sshCloser = new();
    private readonly ThrowingSshCloser _throwingCloser = new();
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        _service = new SessionService(_client, _secrets, _credentials, _sshCloser, TestSupport.Policy(), _time, NullLogger<SessionService>.Instance);
    }

    private static AuthCallResult Tokens(string access, string refresh, int validMinutes = 15) =>
        AuthCallResult.Success(new AuthTokens(access, Start.AddMinutes(validMinutes), refresh));

    private void Seed(string access, string refresh)
    {
        _secrets.Values[SecretSlot.CortexaAccessToken] = access;
        _secrets.Values[SecretSlot.CortexaRefreshToken] = refresh;
    }

    [Fact]
    public async Task Sign_in_stores_both_tokens_and_becomes_signed_in()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");

        var outcome = await _service.SignInAsync(" user@example.com ", Password, TestSupport.Ct);

        Assert.IsType<SignInOutcome.Success>(outcome);
        Assert.Equal(SessionState.SignedIn, _service.Current);
        Assert.Equal(Email, _service.UserEmail);
        Assert.Equal(Start.AddMinutes(15), _service.ExpiresAt);
        Assert.Equal("access-1", _secrets.Values[SecretSlot.CortexaAccessToken]);
        Assert.Equal("refresh-1", _secrets.Values[SecretSlot.CortexaRefreshToken]);
        Assert.Equal("access-1", await _service.GetAccessTokenAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task Sign_in_with_invalid_input_makes_no_network_call()
    {
        var outcome = await _service.SignInAsync("not-an-email", Password, TestSupport.Ct);

        var invalid = Assert.IsType<SignInOutcome.InvalidInput>(outcome);
        Assert.Equal(SignInField.Email, invalid.Field);
        Assert.Equal(0, _client.LoginCalls);
    }

    [Theory]
    [InlineData(AuthFailureKind.InvalidCredentials, typeof(SignInOutcome.InvalidCredentials))]
    [InlineData(AuthFailureKind.Forbidden, typeof(SignInOutcome.Forbidden))]
    [InlineData(AuthFailureKind.AccountLocked, typeof(SignInOutcome.AccountLocked))]
    [InlineData(AuthFailureKind.RateLimited, typeof(SignInOutcome.RateLimited))]
    [InlineData(AuthFailureKind.Unreachable, typeof(SignInOutcome.Unreachable))]
    [InlineData(AuthFailureKind.UnexpectedResponse, typeof(SignInOutcome.UnexpectedResponse))]
    public async Task Sign_in_maps_failures_and_stays_signed_out(AuthFailureKind kind, Type expected)
    {
        _client.LoginResult = AuthCallResult.Fail(kind);

        var outcome = await _service.SignInAsync(Email, Password, TestSupport.Ct);

        Assert.IsType(expected, outcome);
        Assert.Equal(SessionState.SignedOut, _service.Current);
        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task Sign_in_carries_retry_after_for_locked_account()
    {
        var wait = TimeSpan.FromMinutes(15);
        _client.LoginResult = AuthCallResult.Fail(AuthFailureKind.AccountLocked, wait);

        var outcome = await _service.SignInAsync(Email, Password, TestSupport.Ct);

        Assert.Equal(wait, Assert.IsType<SignInOutcome.AccountLocked>(outcome).RetryAfter);
    }

    [Fact]
    public async Task Sign_out_clears_both_slots()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);

        await _service.SignOutAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedOut, _service.Current);
        Assert.Null(_service.UserEmail);
        Assert.Empty(_secrets.Values);
        Assert.Null(await _service.GetAccessTokenAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task Restore_without_stored_tokens_is_signed_out()
    {
        await _service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedOut, _service.Current);
    }

    [Fact]
    public async Task Restore_with_valid_token_signs_in_without_server_call()
    {
        Seed(TestSupport.Jwt(Email, Start.AddMinutes(10)), "refresh-1");

        await _service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedIn, _service.Current);
        Assert.Equal(Email, _service.UserEmail);
        Assert.Equal(Start.AddMinutes(10), _service.ExpiresAt);
        Assert.Equal(0, _client.RefreshCalls);
    }

    [Fact]
    public async Task Restore_with_near_expiry_token_refreshes_first()
    {
        Seed(TestSupport.Jwt(Email, Start.AddSeconds(30)), "refresh-1");
        var renewed = TestSupport.Jwt(Email, Start.AddMinutes(15));
        _client.EnqueueRefresh(Tokens(renewed, "refresh-2"));

        await _service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedIn, _service.Current);
        Assert.Equal(Email, _service.UserEmail);
        Assert.Equal("refresh-1", _client.LastRefreshToken);
        Assert.Equal("refresh-2", _secrets.Values[SecretSlot.CortexaRefreshToken]);
        Assert.Equal(renewed, _secrets.Values[SecretSlot.CortexaAccessToken]);
    }

    [Fact]
    public async Task Restore_with_rejected_refresh_token_is_signed_out_and_cleared()
    {
        Seed(TestSupport.Jwt(Email, Start.AddSeconds(-30)), "refresh-1");
        _client.EnqueueRefresh(AuthCallResult.Fail(AuthFailureKind.InvalidRefreshToken));

        await _service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedOut, _service.Current);
        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task Restore_with_unreachable_gateway_is_signed_out_but_keeps_tokens()
    {
        Seed(TestSupport.Jwt(Email, Start.AddSeconds(-30)), "refresh-1");
        _client.EnqueueRefresh(AuthCallResult.Fail(AuthFailureKind.Unreachable));

        await _service.RestoreAsync(TestSupport.Ct);

        Assert.Equal(SessionState.SignedOut, _service.Current);
        Assert.Equal("refresh-1", _secrets.Values[SecretSlot.CortexaRefreshToken]);
    }

    [Fact]
    public async Task Refresh_swaps_in_the_rotated_refresh_token()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);
        _client.EnqueueRefresh(Tokens("access-2", "refresh-2"));

        var refreshed = await _service.RefreshAsync(TestSupport.Ct);

        Assert.True(refreshed);
        Assert.Equal("refresh-1", _client.LastRefreshToken);
        Assert.Equal("access-2", await _service.GetAccessTokenAsync(TestSupport.Ct));
        Assert.Equal("refresh-2", _secrets.Values[SecretSlot.CortexaRefreshToken]);
        Assert.Equal(Email, _service.UserEmail);
    }

    [Fact]
    public async Task Invalid_refresh_token_expires_session_and_clears_slots()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);
        _client.EnqueueRefresh(AuthCallResult.Fail(AuthFailureKind.InvalidRefreshToken));

        var fresh = await _service.RefreshAfterUnauthorizedAsync("access-1", TestSupport.Ct);

        Assert.Null(fresh);
        Assert.Equal(SessionState.Expired, _service.Current);
        Assert.Empty(_secrets.Values);
        Assert.Null(await _service.GetAccessTokenAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task Transient_refresh_failure_keeps_the_session()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);
        _client.EnqueueRefresh(AuthCallResult.Fail(AuthFailureKind.Unreachable));

        var fresh = await _service.RefreshAfterUnauthorizedAsync("access-1", TestSupport.Ct);

        Assert.Null(fresh);
        Assert.Equal(SessionState.SignedIn, _service.Current);
        Assert.Equal("refresh-1", _secrets.Values[SecretSlot.CortexaRefreshToken]);
    }

    [Fact]
    public async Task Three_parallel_refreshes_make_one_server_call()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);
        _client.EnqueueRefresh(Tokens("access-2", "refresh-2"));
        _client.RefreshGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, 3)
            .Select(_ => _service.RefreshAfterUnauthorizedAsync("access-1", TestSupport.Ct))
            .ToArray();
        _client.RefreshGate.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.Equal(1, _client.RefreshCalls);
        Assert.All(results, token => Assert.Equal("access-2", token));
    }

    [Fact]
    public async Task Mark_expired_clears_slots_and_raises_changed()
    {
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);
        var raised = 0;
        _service.Changed += (_, _) => raised++;

        await _service.MarkExpiredAsync(TestSupport.Ct);

        Assert.Equal(SessionState.Expired, _service.Current);
        Assert.Empty(_secrets.Values);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Sign_out_clears_the_session_credentials()
    {
        SeedCredentials();
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);

        await _service.SignOutAsync(TestSupport.Ct);

        Assert.Null(_credentials.GetToken(SourceType.Github));
        Assert.Null(_credentials.GetToken(SourceType.AzureDevops));
        Assert.Null(_credentials.GetSshPassphrase());
        Assert.Equal(1, _sshCloser.Closed);
    }

    [Fact]
    public async Task Mark_expired_clears_the_session_credentials()
    {
        SeedCredentials();
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await _service.SignInAsync(Email, Password, TestSupport.Ct);

        await _service.MarkExpiredAsync(TestSupport.Ct);

        Assert.Null(_credentials.GetToken(SourceType.Github));
        Assert.Null(_credentials.GetSshPassphrase());
        Assert.Equal(1, _sshCloser.Closed);
    }

    private void SeedCredentials()
    {
        _credentials.SetToken(SourceType.Github, "ghp_token");
        _credentials.SetToken(SourceType.AzureDevops, "ado_token");
        _credentials.SetSshPassphrase("phrase");
    }

    [Fact]
    public async Task Sign_out_deletes_the_secrets_even_when_closing_ssh_throws()
    {
        var service = new SessionService(_client, _secrets, _credentials, _throwingCloser, TestSupport.Policy(), _time, NullLogger<SessionService>.Instance);
        _client.LoginResult = Tokens("access-1", "refresh-1");
        await service.SignInAsync(Email, Password, TestSupport.Ct);

        await service.SignOutAsync(TestSupport.Ct);

        Assert.Empty(_secrets.Values);
        Assert.Equal(SessionState.SignedOut, service.Current);
    }

    private sealed class ThrowingSshCloser : Collector.Application.Ports.ISshConnectionCloser
    {
        public Task CloseAsync(CancellationToken cancellationToken) => throw new ObjectDisposedException("gate");
    }
}
