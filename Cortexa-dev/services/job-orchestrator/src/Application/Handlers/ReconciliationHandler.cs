using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class ReconciliationHandler : IReconciliationHandler
{
    private const string SystemOperator = "system:reconciliation";

    private readonly ReconciliationScannerDependencies _deps;
    private readonly DeleteBatchHandler _deleter;
    private readonly ReconciliationOptions _options;
    private readonly ILogger<ReconciliationHandler> _logger;

    public ReconciliationHandler(
        ReconciliationScannerDependencies deps,
        DeleteBatchHandler deleter,
        IOptions<ReconciliationOptions> options,
        ILogger<ReconciliationHandler> logger)
    {
        _deps = deps;
        _deleter = deleter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ScanAsync(CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var acc = await RunScanAsync(ct);
        return BuildReport(startedUtc, dryRun: true, acc);
    }

    public async Task<ReconciliationReport> ReconcileAsync(bool confirmed, CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var acc = await RunScanAsync(ct);

        if (!confirmed || !_options.ReconcileEnabled)
            return BuildReport(startedUtc, dryRun: true, acc);

        await ExecuteReconciliationAsync(acc, ct);
        return BuildReport(startedUtc, dryRun: false, acc);
    }

    private async Task<ReconciliationAccumulator> RunScanAsync(CancellationToken ct)
    {
        var acc = new ReconciliationAccumulator();
        var liveIds = await _deps.ExistenceQuery.GetLiveBatchIdsAsync(ct);

        foreach (var scanner in _deps.Scanners)
        {
            var scanned = await scanner.ListBatchIdsAsync(ct);
            var orphaned = scanned.Except(liveIds).ToList();
            acc.PerStore.Add(new StoreOrphanSummary(scanner.StoreName, orphaned.Count, orphaned));
        }

        var stuckIds = await _deps.StuckScanner.ListStuckBatchIdsAsync(ct);
        var stuckOrphaned = stuckIds.Except(liveIds).ToList();
        acc.PerStore.Add(new StoreOrphanSummary("servicebus-stuck", stuckOrphaned.Count, stuckOrphaned));

        acc.Dangling.AddRange(await _deps.ReferenceScanner.FindDanglingAsync(ct));

        _logger.LogInformation(
            "Reconciliation scan complete. total_orphan_batches={TotalOrphanBatches} dangling_refs={DanglingCount}",
            CountDistinctOrphans(acc),
            acc.Dangling.Count);

        return acc;
    }

    private async Task ExecuteReconciliationAsync(ReconciliationAccumulator acc, CancellationToken ct)
    {
        var distinctOrphans = acc.PerStore
            .SelectMany(s => s.BatchIds)
            .Distinct()
            .Take(_options.MaxReconcilesPerRun)
            .ToList();

        _logger.LogInformation(
            "Reconciliation delete starting. candidate_count={Count} max_per_run={Max}",
            distinctOrphans.Count,
            _options.MaxReconcilesPerRun);

        foreach (var batchId in distinctOrphans)
        {
            if (ct.IsCancellationRequested)
                break;

            await TryReconcileBatchAsync(batchId, acc, ct);
        }

        _logger.LogInformation(
            "Reconciliation delete complete. reconciled={Count} errors={Errors}",
            acc.ReconciledCount,
            acc.Errors.Count);
    }

    private async Task TryReconcileBatchAsync(
        string batchId,
        ReconciliationAccumulator acc,
        CancellationToken ct)
    {
        try
        {
            var options = new DeletionRequestOptions(SystemOperator, Guid.NewGuid().ToString(), Force: true);
            var context = new DeleteBatchContext(batchId, options, Access: BatchAccess.Unrestricted);
            var result = await _deleter.HandleAsync(context, ct);

            foreach (var store in result.Stores)
            {
                if (!acc.PerStoreDeleted.TryAdd(store.StoreName, store.DeletedCount))
                    acc.PerStoreDeleted[store.StoreName] += store.DeletedCount;
            }

            if (result.FullyDeleted)
                acc.ReconciledCount++;
            else
                acc.Errors.Add($"batch_id={batchId} partial_delete");
        }
        catch (Exception ex)
        {
            acc.Errors.Add($"batch_id={batchId} error={ex.Message}");
            _logger.LogWarning(
                "Reconciliation failed to delete batch. batch_id={BatchId} reason={Reason}",
                batchId,
                ex.Message);
        }
    }

    private ReconciliationReport BuildReport(DateTimeOffset startedUtc, bool dryRun, ReconciliationAccumulator acc)
    {
        var totalOrphanBatches = CountDistinctOrphans(acc);
        var severity = ComputeSeverity(totalOrphanBatches);

        return new ReconciliationReport(
            startedUtc,
            dryRun,
            acc.PerStore,
            acc.Dangling,
            totalOrphanBatches,
            severity,
            acc.ReconciledCount,
            acc.PerStoreDeleted,
            acc.Errors,
            DateTimeOffset.UtcNow - startedUtc);
    }

    private ReconciliationSeverity ComputeSeverity(int totalOrphanBatches)
    {
        if (totalOrphanBatches >= _options.BlockerOrphanThreshold)
            return ReconciliationSeverity.Blocker;

        return totalOrphanBatches > 0 ? ReconciliationSeverity.Warning : ReconciliationSeverity.Info;
    }

    private static int CountDistinctOrphans(ReconciliationAccumulator acc) =>
        acc.PerStore.SelectMany(s => s.BatchIds).Distinct().Count();
}
