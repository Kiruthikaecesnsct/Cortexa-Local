using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.BackgroundServices;

public sealed class RetentionSweepBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionPolicyOptions _options;
    private readonly ILogger<RetentionSweepBackgroundService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RetentionSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionPolicyOptions> options,
        ILogger<RetentionSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Retention sweeper is disabled. Background service will not run.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TryRunSweepAsync(stoppingToken);
    }

    private async Task TryRunSweepAsync(CancellationToken stoppingToken)
    {
        if (!_gate.Wait(0))
        {
            _logger.LogWarning("Retention sweep skipped — previous sweep is still running.");
            return;
        }

        try
        {
            await RunSweepAsync(stoppingToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSweepAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IRetentionSweeper>();

        try
        {
            var report = await sweeper.SweepAsync(null, stoppingToken);

            _logger.LogInformation(
                "Retention sweep finished. dry_run={DryRun} deleted={Deleted} errors={Errors} elapsed_ms={ElapsedMs}",
                report.DryRun,
                report.DeletedCount,
                report.Errors.Count,
                (long)report.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Retention sweep cancelled due to host shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention sweep encountered an unhandled error.");
        }
    }
}
