using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.BackgroundServices;

public sealed class StaleBatchWatchdogBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WatchdogOptions _options;
    private readonly ILogger<StaleBatchWatchdogBackgroundService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StaleBatchWatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<WatchdogOptions> options,
        ILogger<StaleBatchWatchdogBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WatchdogEnabled)
        {
            _logger.LogInformation("Stale-batch watchdog is disabled. Background service will not run.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.WatchdogIntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TryRunWatchdogAsync(stoppingToken);
    }

    private async Task TryRunWatchdogAsync(CancellationToken stoppingToken)
    {
        if (!_gate.Wait(0))
        {
            _logger.LogWarning("Stale-batch watchdog run skipped — previous run is still in progress.");
            return;
        }

        try
        {
            await RunWatchdogAsync(stoppingToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunWatchdogAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<StaleBatchWatchdogHandler>();

        try
        {
            var failedCount = await handler.RunAsync(stoppingToken);

            _logger.LogInformation(
                "Stale-batch watchdog run finished. batches_auto_failed={BatchesAutoFailed}",
                failedCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Stale-batch watchdog run cancelled due to host shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stale-batch watchdog run encountered an unhandled error.");
        }
    }
}
