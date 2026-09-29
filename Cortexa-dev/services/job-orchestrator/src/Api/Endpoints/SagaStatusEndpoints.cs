using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Mapping;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class SagaStatusEndpoints
{
    public static void MapSagaStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/batches/{id}/status", HandleGetStatus).RequireAuthorization();
    }

    private static async Task<IResult> HandleGetStatus(
        string id,
        ISagaRepository sagaRepo,
        IDocumentRepository documentRepo,
        IOptions<OrchestratorSettings> settings,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        var saga = await sagaRepo.GetAsync(id, ct);

        if (saga is null)
            return Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", $"Batch '{id}' was not found.", correlationId));

        var docRecords = await documentRepo.ListByBatchAsync(id, ct);
        var filenameMap = docRecords.ToDictionary(r => r.DocumentId, r => r.Filename);

        var documents = saga.Documents
            .Select(d => new BatchDocumentStatusDto
            {
                DocumentId = d.DocumentId,
                Filename = filenameMap.TryGetValue(d.DocumentId, out var fn) ? fn : d.DocumentId,
                Status = StatusMapper.MapDocumentStatus(d.State),
                Error = d.FailureReason
            })
            .ToList();

        var unavailableSources = IsTerminal(saga.State)
            ? StatusMapper.ComputeUnavailableSources(saga, settings.Value.EvidenceSourceOutageThreshold)
            : Array.Empty<string>();

        var response = new BatchStatusResponse
        {
            BatchId = saga.Id,
            BatchName = saga.Metadata?.BatchName,
            Status = StatusMapper.MapBatchStatus(saga),
            WantsHarvesting = saga.WantsHarvesting,
            WantsSeeding = saga.WantsSeeding,
            SeedingMode = saga.Metadata?.SeedingMode,
            CreatedAt = saga.Metadata?.CreatedAt?.ToString("O"),
            Documents = documents,
            UnavailableSources = unavailableSources
        };

        return Results.Ok(ApiResponse<BatchStatusResponse>.Ok(response, correlationId));
    }

    private static bool IsTerminal(BatchState state)
    {
        return state is BatchState.Completed or BatchState.Failed or BatchState.Cancelled;
    }
}
