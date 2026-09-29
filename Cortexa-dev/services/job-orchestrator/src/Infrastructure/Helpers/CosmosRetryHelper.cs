using System.Diagnostics;
using System.Net.Sockets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Cortexa.JobOrchestrator.Infrastructure.Helpers;

public static class CosmosRetryHelper
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(10);

    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        IRetryPolicy policy,
        TimeSpan? totalBudget = null,
        CancellationToken ct = default,
        CancellationToken? externalCt = null)
    {
        var budget = totalBudget ?? DefaultBudget;
        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransient(ex, externalCt) && policy.ShouldRetry(attempt) && stopwatch.Elapsed < budget)
            {
                var baseDelay = policy.DelayFor(attempt);
                // Random.Shared is thread-safe (.NET 6+); concurrent deletes (Task.WhenAll,
                // retention sweep) call this from multiple threads. Jitter caps at 10% of the
                // base delay to spread retries and avoid a thundering herd against Cosmos.
                var jitterCeilingMs = Math.Max(1, (int)(baseDelay.TotalMilliseconds * 0.1));
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, jitterCeilingMs));
                var delay = baseDelay + jitter;
                var remaining = budget - stopwatch.Elapsed;
                if (delay > remaining)
                    throw;

                await Task.Delay(delay, ct);
            }
        }
    }

    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        IRetryPolicy policy,
        TimeSpan? totalBudget = null,
        CancellationToken ct = default,
        CancellationToken? externalCt = null)
    {
        await ExecuteWithRetryAsync(
            async () =>
            {
                await action();
                return true;
            },
            policy,
            totalBudget,
            ct,
            externalCt);
    }

    private static bool IsTransient(Exception ex, CancellationToken? externalCt)
    {
        return ex switch
        {
            CosmosException cosmosEx => IsTransientCosmosStatus((int)cosmosEx.StatusCode),
            HttpRequestException => true,
            SocketException => true,
            TimeoutException => true,
            TaskCanceledException => IsCancellationTransient(externalCt),
            OperationCanceledException => IsCancellationTransient(externalCt),
            _ => false
        };
    }

    private static bool IsCancellationTransient(CancellationToken? externalCt)
    {
        if (externalCt is null)
            return false;

        return !externalCt.Value.IsCancellationRequested;
    }

    private static bool IsTransientCosmosStatus(int statusCode)
    {
        return statusCode switch
        {
            408 => true,
            429 => true,
            503 => true,
            _ => false
        };
    }
}
