using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Retention;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Application.Handlers;

/// <summary>
/// Cosmos TTL only removes the batches item and will not touch Blob Storage, Key Vault, or
/// Service Bus artifacts. This orchestrated cascade sweep is therefore still required to drive
/// the full cross-store deletion via DeleteBatchHandler for every eligible batch.
/// </summary>
public sealed class RetentionSweepHandler : IRetentionSweeper
{
    private const string SystemOperator = "system:retention-sweeper";

    private readonly IBatchRetentionQuery _query;
    private readonly DeleteBatchHandler _deleter;
    private readonly RetentionPolicyOptions _options;
    private readonly ILogger<RetentionSweepHandler> _logger;

    public RetentionSweepHandler(
        IBatchRetentionQuery query,
        DeleteBatchHandler deleter,
        IOptions<RetentionPolicyOptions> options,
        ILogger<RetentionSweepHandler> logger)
    {
        _query = query;
        _deleter = deleter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RetentionSweepReport> SweepAsync(bool? dryRunOverride, CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var dryRun = dryRunOverride ?? _options.DryRun;
        var candidates = await CollectCandidatesAsync(startedUtc, ct);

        var perStateFound = candidates
            .GroupBy(c => c.State)
            .ToDictionary(g => g.Key, g => g.Count());

        _logger.LogInformation(
            "Retention sweep started. dry_run={DryRun} candidates={Count} per_state={@PerState}",
            dryRun,
            candidates.Count,
            perStateFound);

        var emptyAcc = new RetentionSweepAccumulator(
            candidates, 0, new Dictionary<string, int>(), new List<string>());

        if (dryRun)
            return BuildReport(startedUtc, dryRun, emptyAcc);

        return await ExecuteDeletionsAsync(startedUtc, dryRun, candidates, ct);
    }

    private async Task<List<RetentionCandidate>> CollectCandidatesAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var failedCutoff = nowUtc.AddDays(-_options.FailedRetentionDays);
        var stuckCutoff = nowUtc.AddHours(-_options.IncompleteStuckHours);
        var all = new List<RetentionCandidate>();

        all.AddRange(await CollectPagedAsync(
            (page, token) => _query.FindFailedOlderThanAsync(failedCutoff, page, token), ct));

        all.AddRange(await CollectPagedAsync(
            (page, token) => _query.FindStuckIncompleteAsync(stuckCutoff, page, token), ct));

        if (_options.ForceWedgedRunning)
            all.AddRange(await CollectWedgedRunningAsync(nowUtc, ct));

        return all;
    }

    private async Task<List<RetentionCandidate>> CollectPagedAsync(
        Func<int, CancellationToken, Task<IReadOnlyList<RetentionCandidate>>> pageFunc,
        CancellationToken ct)
    {
        var all = new List<RetentionCandidate>();
        var page = 0;

        while (true)
        {
            var batch = await pageFunc(page, ct);
            all.AddRange(batch);

            if (batch.Count < _options.BatchPageSize)
                break;

            page++;
        }

        return all;
    }

    private async Task<List<RetentionCandidate>> CollectWedgedRunningAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var stuckCutoff = nowUtc.AddHours(-_options.IncompleteStuckHours);
        var inProgressCandidates = await CollectPagedAsync(
            (page, token) => _query.FindStuckInProgressAsync(stuckCutoff, page, token), ct);

        return inProgressCandidates
            .Where(c => RetentionRules.IsWedgedRunning(
                c.LastActivityUtc, nowUtc, _options.IncompleteStuckHours, _options.ForceWedgedRunning))
            .ToList();
    }

    private async Task<RetentionSweepReport> ExecuteDeletionsAsync(
        DateTimeOffset startedUtc,
        bool dryRun,
        List<RetentionCandidate> candidates,
        CancellationToken ct)
    {
        var perStoreDeleted = new Dictionary<string, int>();
        var errors = new List<string>();
        var deletedCount = 0;

        foreach (var candidate in candidates.Take(_options.MaxDeletesPerRun))
        {
            if (ct.IsCancellationRequested)
                break;

            var deleted = await TryDeleteCandidateAsync(candidate, perStoreDeleted, errors, ct);
            if (deleted)
                deletedCount++;
        }

        var acc = new RetentionSweepAccumulator(candidates, deletedCount, perStoreDeleted, errors);
        LogSweepCompletion(deletedCount, errors.Count, DateTimeOffset.UtcNow - startedUtc);
        return BuildReport(startedUtc, dryRun, acc);
    }

    private async Task<bool> TryDeleteCandidateAsync(
        RetentionCandidate candidate,
        Dictionary<string, int> perStoreDeleted,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var force = !IsTerminalState(candidate.State);
            var options = new DeletionRequestOptions(SystemOperator, Guid.NewGuid().ToString(), force);
            var context = new DeleteBatchContext(candidate.BatchId, options, BatchAccess.Unrestricted);

            var result = await _deleter.HandleAsync(context, ct);

            foreach (var store in result.Stores)
            {
                if (!perStoreDeleted.TryAdd(store.StoreName, store.DeletedCount))
                    perStoreDeleted[store.StoreName] += store.DeletedCount;
            }

            if (!result.FullyDeleted)
                errors.Add($"batch_id={candidate.BatchId} partial_delete");

            return result.FullyDeleted;
        }
        catch (Exception ex)
        {
            errors.Add($"batch_id={candidate.BatchId} error={ex.Message}");
            _logger.LogWarning(
                "Retention sweep failed to delete batch. batch_id={BatchId} reason={Reason}",
                candidate.BatchId,
                ex.Message);
            return false;
        }
    }

    private void LogSweepCompletion(int deletedCount, int errorCount, TimeSpan elapsed)
    {
        _logger.LogInformation(
            "Retention sweep completed. deleted={DeletedCount} errors={ErrorCount} elapsed_ms={ElapsedMs}",
            deletedCount,
            errorCount,
            (long)elapsed.TotalMilliseconds);
    }

    private static RetentionSweepReport BuildReport(
        DateTimeOffset startedUtc,
        bool dryRun,
        RetentionSweepAccumulator acc)
    {
        var perStateFound = acc.Candidates
            .GroupBy(c => c.State)
            .ToDictionary(g => g.Key, g => g.Count());

        return new RetentionSweepReport(
            startedUtc,
            dryRun,
            perStateFound,
            acc.Candidates,
            acc.DeletedCount,
            acc.PerStoreDeleted,
            acc.Errors,
            DateTimeOffset.UtcNow - startedUtc);
    }

    private static bool IsTerminalState(string state) =>
        state is "Completed" or "Failed" or "Cancelled";
}
