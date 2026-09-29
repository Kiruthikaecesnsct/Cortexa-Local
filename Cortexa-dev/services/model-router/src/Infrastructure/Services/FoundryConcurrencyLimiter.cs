using System.Collections.Concurrent;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sentry;

namespace Cortexa.ModelRouter.Infrastructure.Services;

public interface IFoundryConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(string deployment, int maxInFlight, int waitSeconds, CancellationToken ct);
}

public sealed class FoundryConcurrencyLimiter : IFoundryConcurrencyLimiter
{
    private const double DefaultHighWaterFraction = 0.8;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly ILogger<FoundryConcurrencyLimiter> _logger;
    private readonly double _highWaterFraction;

    public FoundryConcurrencyLimiter(
        ILogger<FoundryConcurrencyLimiter> logger,
        IOptions<FoundrySettings> settings)
    {
        _logger = logger;
        _highWaterFraction = settings.Value.ConcurrencyHighWaterFraction;
    }

    public FoundryConcurrencyLimiter()
        : this(NullLogger<FoundryConcurrencyLimiter>.Instance, Options.Create(new FoundrySettings { ConcurrencyHighWaterFraction = DefaultHighWaterFraction }))
    {
    }

    public async Task<IDisposable> AcquireAsync(string deployment, int maxInFlight, int waitSeconds, CancellationToken ct)
    {
        var semaphore = _semaphores.GetOrAdd(deployment, _ => new SemaphoreSlim(maxInFlight, maxInFlight));
        var acquired = await TryAcquireAsync(semaphore, waitSeconds, ct);
        if (!acquired)
            throw new FoundryCapacityExceededException(deployment, maxInFlight);

        CheckHighWater(deployment, maxInFlight, semaphore);
        return new SemaphoreRelease(semaphore);
    }

    public int GetInFlightCount(string deployment, int maxInFlight)
    {
        var semaphore = _semaphores.GetOrAdd(deployment, _ => new SemaphoreSlim(maxInFlight, maxInFlight));
        return maxInFlight - semaphore.CurrentCount;
    }

    private void CheckHighWater(string deployment, int maxInFlight, SemaphoreSlim semaphore)
    {
        var inFlight = maxInFlight - semaphore.CurrentCount;
        var threshold = maxInFlight * _highWaterFraction;
        if (inFlight < threshold)
            return;

        _logger.LogWarning(
            "Foundry concurrency high-water reached: deployment={Deployment} inFlight={InFlight} maxInFlight={MaxInFlight} highWaterFraction={HighWaterFraction}",
            deployment,
            inFlight,
            maxInFlight,
            _highWaterFraction);

        EmitHighWaterMetric(deployment, inFlight);
    }

    private static void EmitHighWaterMetric(string deployment, int inFlight)
    {
        var metricName = $"foundry.concurrency.high_water.{deployment}";
        SentrySdk.Metrics.EmitGauge(metricName, inFlight, MeasurementUnit.None);
    }

    private static async Task<bool> TryAcquireAsync(SemaphoreSlim semaphore, int waitSeconds, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(waitSeconds));
        try
        {
            await semaphore.WaitAsync(timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed class SemaphoreRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public SemaphoreRelease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _semaphore.Release();
        }
    }
}
