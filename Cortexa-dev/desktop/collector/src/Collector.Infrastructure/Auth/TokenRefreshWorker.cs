using Collector.Application.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Collector.Infrastructure.Auth;

public sealed class TokenRefreshWorker(
    ISessionState session,
    IAccessTokenProvider tokens,
    TokenRefreshPolicy policy,
    TimeProvider time,
    ILogger<TokenRefreshWorker> logger) : BackgroundService
{
    private TaskCompletionSource _wake = NewWake();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        session.Changed += OnChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            session.Changed -= OnChanged;
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var wake = NewWake();
        Volatile.Write(ref _wake, wake);

        var delay = ComputeDelay();
        if (delay is null)
        {
            await wake.Task.WaitAsync(stoppingToken);
            return;
        }

        if (await WaitForDueAsync(delay.Value, wake.Task, stoppingToken))
        {
            await TryRefreshAsync(stoppingToken);
        }
    }

    private TimeSpan? ComputeDelay()
    {
        if (session.Current != SessionState.SignedIn || session.ExpiresAt is not { } expiresAt)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var next = policy.NextRefreshAt(expiresAt, now);
        return next > now ? next - now : TimeSpan.Zero;
    }

    private async Task<bool> WaitForDueAsync(TimeSpan delay, Task wake, CancellationToken stoppingToken)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var due = Task.Delay(delay, time, delayCts.Token);
        var winner = await Task.WhenAny(due, wake);
        if (winner == due)
        {
            await due;
            return true;
        }

        await delayCts.CancelAsync();
        stoppingToken.ThrowIfCancellationRequested();
        return false;
    }

    private async Task TryRefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            await tokens.RefreshAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Scheduled token refresh failed: {ErrorType}.", ex.GetType().Name);
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Volatile.Read(ref _wake).TrySetResult();

    private static TaskCompletionSource NewWake() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
