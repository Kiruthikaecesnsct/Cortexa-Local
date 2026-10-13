using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Support;

internal static class OAuthData
{
    public const string DeviceCode = "device-code-value";
    public const string UserCode = "WDJB-MJHT";

    public static readonly Uri VerificationUri = new("https://github.com/login/device");

    public static DeviceCodeGrant Grant(TimeSpan? interval = null, TimeSpan? expiresIn = null) => new(
        DeviceCode,
        UserCode,
        VerificationUri,
        expiresIn ?? TimeSpan.FromMinutes(15),
        interval ?? TimeSpan.FromSeconds(5));

    public static OAuthTokenSet Tokens(string access, DateTimeOffset? accessExpiresAt = null, string? refresh = "refresh", DateTimeOffset? refreshExpiresAt = null) =>
        new(access, refresh, accessExpiresAt, refreshExpiresAt);
}

internal sealed class ScriptedDeviceClient(FakeTimeProvider time) : IDeviceAuthorizationClient
{
    private readonly Queue<DevicePollResult> _polls = new();
    private int _refreshCalls;
    private readonly List<DateTimeOffset> _pollTimes = [];

    public Func<CancellationToken, Task>? OnPoll { get; set; }

    public Func<Task<RefreshResult>>? OnRefresh { get; set; }

    public RefreshResult RefreshResult { get; set; } = new RefreshResult.Rejected();

    public int RefreshCalls => Volatile.Read(ref _refreshCalls);

    public IReadOnlyList<DateTimeOffset> PollTimes
    {
        get
        {
            lock (_pollTimes)
            {
                return [.. _pollTimes];
            }
        }
    }

    public void Enqueue(params DevicePollResult[] results)
    {
        foreach (var result in results)
        {
            _polls.Enqueue(result);
        }
    }

    public Task<DeviceCodeGrant> RequestCodeAsync(RemoteAuthMethod method, CancellationToken cancellationToken) =>
        Task.FromResult(OAuthData.Grant());

    public async Task<DevicePollResult> PollAsync(RemoteAuthMethod method, string deviceCode, CancellationToken cancellationToken)
    {
        lock (_pollTimes)
        {
            _pollTimes.Add(time.GetUtcNow());
        }

        if (OnPoll is not null)
        {
            await OnPoll(cancellationToken);
        }

        return _polls.Dequeue();
    }

    public Task<RefreshResult> RefreshAsync(RemoteAuthMethod method, string refreshToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        return OnRefresh is null ? Task.FromResult(RefreshResult) : OnRefresh();
    }
}

internal sealed class InMemoryOAuthVault : IOAuthTokenVault
{
    private readonly Dictionary<RemoteAuthMethod, OAuthTokenSet> _sets = [];
    private readonly List<string> _log = [];

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_sets)
            {
                return [.. _log];
            }
        }
    }

    public int SaveCalls => Log.Count(entry => entry.StartsWith("save:", StringComparison.Ordinal));

    public int DropCalls { get; private set; }

    public OAuthTokenSet? Peek(RemoteAuthMethod method)
    {
        lock (_sets)
        {
            return _sets.GetValueOrDefault(method);
        }
    }

    public void Seed(RemoteAuthMethod method, OAuthTokenSet tokens)
    {
        lock (_sets)
        {
            _sets[method] = tokens;
        }
    }

    public Task<OAuthTokenSet?> ReadAsync(RemoteAuthMethod method, CancellationToken cancellationToken) =>
        Task.FromResult(Peek(method));

    public Task SaveAsync(RemoteAuthMethod method, OAuthTokenSet tokens, CancellationToken cancellationToken)
    {
        lock (_sets)
        {
            _sets[method] = tokens;
            _log.Add($"save:{method}");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(RemoteAuthMethod method, CancellationToken cancellationToken)
    {
        lock (_sets)
        {
            _sets.Remove(method);
            _log.Add($"delete:{method}");
        }

        return Task.CompletedTask;
    }

    public Task DeleteProviderAsync(SourceType provider, CancellationToken cancellationToken)
    {
        lock (_sets)
        {
            _sets.Clear();
            _log.Add($"deleteProvider:{provider}");
        }

        return Task.CompletedTask;
    }

    public void DropCache() => DropCalls++;
}
