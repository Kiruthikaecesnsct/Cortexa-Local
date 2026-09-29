using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class StaleBatchWatchdogHandler
{
    private readonly ISagaRepository _repository;
    private readonly WatchdogOptions _options;
    private readonly ILogger<StaleBatchWatchdogHandler> _logger;

    public StaleBatchWatchdogHandler(
        ISagaRepository repository,
        IOptions<WatchdogOptions> options,
        ILogger<StaleBatchWatchdogHandler> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoffUtc = nowUtc.AddMinutes(-_options.StageStallSlaMinutes);

        var stalledBatchIds = await _repository.ListStalledBatchIdsAsync(cutoffUtc, ct);
        var failedCount = 0;

        foreach (var batchId in stalledBatchIds)
        {
            if (ct.IsCancellationRequested)
                break;

            if (await TryFailStalledBatchAsync(batchId, nowUtc, ct))
                failedCount++;
        }

        return failedCount;
    }

    private async Task<bool> TryFailStalledBatchAsync(string batchId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        try
        {
            var saga = await _repository.GetAsync(batchId, ct);

            if (saga is null || saga.State != BatchState.InProgress)
                return false;

            var reason = $"stalled: no progress for {_options.StageStallSlaMinutes} minutes";

            saga.MarkFailed(reason, nowUtc);
            await _repository.UpdateAsync(saga, ct);

            _logger.LogWarning(
                "batch_stalled batch_id={BatchId} stage_stall_sla_minutes={StageStallSlaMinutes} auto_failed_at={AutoFailedAt}",
                batchId,
                _options.StageStallSlaMinutes,
                nowUtc);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Stale-batch watchdog failed to auto-fail batch. batch_id={BatchId}",
                batchId);
            return false;
        }
    }
}
