using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Cortexa.JobOrchestrator.Application.Handlers;

/// <summary>
/// Orchestrates a best-effort, idempotent cascade delete of a batch across every store.
/// Each store deleter runs concurrently; the saga (batches) document is removed last and
/// only when every other store succeeded, which keeps a partially-failed delete resumable.
/// Synchronous but bounded (store deleters are bounded; frontend has axios timeout).
/// The finalizing saga delete is decoupled from request-abort and runs under a server-owned deadline.
/// </summary>
public sealed class DeleteBatchHandler
{
    private const string SagaStore = "CosmosBatches";

    private readonly ISagaRepository _sagas;
    private readonly IEnumerable<IBatchDeleter> _deleters;
    private readonly ILogger<DeleteBatchHandler> _logger;

    public DeleteBatchHandler(
        ISagaRepository sagas,
        IEnumerable<IBatchDeleter> deleters,
        ILogger<DeleteBatchHandler> logger)
    {
        _sagas = sagas;
        _deleters = deleters;
        _logger = logger;
    }

    public async Task<DeleteBatchResponse> HandleAsync(DeleteBatchContext context, CancellationToken ct)
    {
        var saga = await _sagas.GetAsync(context.BatchId, ct);
        EnsureAuthorized(saga, context);
        EnsureDeletable(saga, context);

        var results = await RunDeletersAsync(context, ct);

        if (results.All(result => result.Success))
            await DeleteSagaAsync(context.BatchId, saga, results, ct);

        var response = BuildResponse(context.BatchId, results);
        LogAudit(context, response, "delete");
        return response;
    }

    private void EnsureAuthorized(BatchSaga? saga, DeleteBatchContext context)
    {
        if (context.Access.IsSuperAdmin)
            return;

        if (saga is null)
        {
            LogDenied(context, "not_found");
            throw new BatchNotFoundException(context.BatchId);
        }

        if (IsSameOrg(saga.Metadata?.OwnerOrgId, context.Access.CallerOrgId))
            return;

        LogDenied(context, "cross_org");
        throw new CrossOrgAccessException(context.BatchId);
    }

    private static bool IsSameOrg(string? ownerOrgId, string? callerOrgId) =>
        ownerOrgId is not null && string.Equals(ownerOrgId, callerOrgId, StringComparison.Ordinal);

    private void LogDenied(DeleteBatchContext context, string reason)
    {
        _logger.LogWarning(
            "Batch delete denied. batch_id={BatchId} org_id={OrgId} user_id={UserId} action={Action} reason={Reason}",
            context.BatchId,
            context.Access.CallerOrgId,
            context.Operator,
            "delete_denied",
            reason);
    }

    private static void EnsureDeletable(BatchSaga? saga, DeleteBatchContext context)
    {
        if (saga is null || context.Force || IsTerminal(saga.State))
            return;

        throw new InvalidBatchStateException(context.BatchId);
    }

    private async Task<List<StoreDeletionResult>> RunDeletersAsync(DeleteBatchContext context, CancellationToken ct)
    {
        var tasks = _deleters.Select(deleter => deleter.DeleteAsync(context.BatchId, context, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task DeleteSagaAsync(
        string batchId,
        BatchSaga? saga,
        List<StoreDeletionResult> results,
        CancellationToken ct)
    {
        try
        {
            await _sagas.DeleteAsync(batchId, ct);
            results.Add(new StoreDeletionResult(SagaStore, saga is null ? 0 : 1, true));
        }
        catch (Exception ex)
        {
            results.Add(new StoreDeletionResult(SagaStore, 0, false, ex.Message));
            _logger.LogError(
                "Batch delete store failed. batch_id={BatchId} store={Store} status={Status} reason={Reason}",
                batchId,
                SagaStore,
                "failed",
                ex.Message);
        }
    }

    private static DeleteBatchResponse BuildResponse(string batchId, IReadOnlyList<StoreDeletionResult> results)
    {
        var fullyDeleted = results.All(result => result.Success);
        var status = fullyDeleted ? "Deleted" : "PartiallyDeleted";
        return new DeleteBatchResponse(batchId, status, fullyDeleted, results);
    }

    private void LogAudit(DeleteBatchContext context, DeleteBatchResponse response, string action)
    {
        var stores = string.Join(
            ",",
            response.Stores.Select(s =>
            {
                var status = s.Success ? "ok" : "fail";
                var reason = s.Error is not null ? $":{s.Error}" : string.Empty;
                return $"{s.StoreName}:{s.DeletedCount}:{status}{reason}";
            }));

        _logger.LogInformation(
            "Batch delete cascade. batch_id={BatchId} org_id={OrgId} user_id={UserId} action={Action} "
            + "operator={Operator} correlation_id={CorrelationId} force={Force} fully_deleted={FullyDeleted} "
            + "stores=[{Stores}]",
            context.BatchId,
            context.Access.CallerOrgId,
            context.Operator,
            action,
            context.Operator,
            context.CorrelationId,
            context.Force,
            response.FullyDeleted,
            stores);

        foreach (var store in response.Stores.Where(s => !s.Success))
        {
            _logger.LogError(
                "Batch delete store failed. batch_id={BatchId} store={Store} status={Status} reason={Reason}",
                context.BatchId,
                store.StoreName,
                "failed",
                store.Error ?? "unknown");
        }
    }

    private static bool IsTerminal(BatchState state) =>
        state is BatchState.Completed or BatchState.Failed or BatchState.Cancelled;
}
