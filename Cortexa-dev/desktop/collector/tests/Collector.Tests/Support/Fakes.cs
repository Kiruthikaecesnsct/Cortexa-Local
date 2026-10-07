using System.Text;
using System.Text.Json;
using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Support;

internal static class TestSupport
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TokenRefreshPolicy Policy(int skewSeconds = 60, int minDelaySeconds = 5) =>
        new(Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            RefreshSkewSeconds = skewSeconds,
            MinRefreshDelaySeconds = minDelaySeconds,
        }));

    public static string Jwt(string? email, DateTimeOffset expiresAt)
    {
        var payload = new Dictionary<string, object?> { ["exp"] = expiresAt.ToUnixTimeSeconds() };
        if (email is not null)
        {
            payload["email"] = email;
        }

        var body = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}")).TrimEnd('=');
        return $"{header}.{body}.signature";
    }
}

internal sealed class InMemorySecretStore : ISecretStore
{
    public Dictionary<SecretSlot, string> Values { get; } = [];

    public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
        Task.FromResult(Values.TryGetValue(slot, out var value) ? value : null);

    public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken)
    {
        Values[slot] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
    {
        Values.Remove(slot);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuthClient : IAuthClient
{
    private readonly Queue<AuthCallResult> _refreshResults = new();

    public int LoginCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public string? LastRefreshToken { get; private set; }

    public AuthCallResult LoginResult { get; set; } = AuthCallResult.Fail(AuthFailureKind.Unreachable);

    public TaskCompletionSource? RefreshGate { get; set; }

    public void EnqueueRefresh(AuthCallResult result) => _refreshResults.Enqueue(result);

    public Task<AuthCallResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        LoginCalls++;
        return Task.FromResult(LoginResult);
    }

    public async Task<AuthCallResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshCalls++;
        LastRefreshToken = refreshToken;
        if (RefreshGate is not null)
        {
            await RefreshGate.Task;
        }

        return _refreshResults.Dequeue();
    }
}

internal sealed class FakeUserSettingsStore : IUserSettingsStore
{
    public EndpointSettings Current { get; private set; } = new("https://gateway.example", "https://server.example");

    public int SaveCalls { get; private set; }

    public EndpointSettings GetEndpoints() => Current;

    public Task SaveEndpointsAsync(EndpointSettings settings, CancellationToken cancellationToken)
    {
        SaveCalls++;
        Current = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSession : ISessionState, IAccessTokenProvider
{
    public SessionState Current { get; set; } = SessionState.SignedOut;

    public string? UserEmail => null;

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? Token { get; set; }

    public string? RefreshedToken { get; set; }

    public int RefreshCalls { get; private set; }

    public int RefreshAfterUnauthorizedCalls { get; private set; }

    public int MarkExpiredCalls { get; private set; }

    public Func<Task>? OnRefresh { get; set; }

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Token);

    public Task<string?> RefreshAfterUnauthorizedAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        RefreshAfterUnauthorizedCalls++;
        return Task.FromResult(RefreshedToken);
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        RefreshCalls++;
        if (OnRefresh is not null)
        {
            await OnRefresh();
        }

        return true;
    }

    public Task MarkExpiredAsync(CancellationToken cancellationToken)
    {
        MarkExpiredCalls++;
        return Task.CompletedTask;
    }
}
