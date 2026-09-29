using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static void MapReconciliationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/reconciliation").RequireAuthorization("BatchOperator");

        group.MapGet("/report", HandleReport);
        group.MapPost("/reconcile", HandleReconcile);
    }

    private static async Task<IResult> HandleReport(
        IReconciliationHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var report = await handler.ScanAsync(ct);
        return Results.Ok(ApiResponse<ReconciliationReport>.Ok(report, correlationId));
    }

    private static async Task<IResult> HandleReconcile(
        IReconciliationHandler handler,
        HttpContext ctx,
        bool? confirm,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var report = await handler.ReconcileAsync(confirm ?? false, ct);
        return Results.Ok(ApiResponse<ReconciliationReport>.Ok(report, correlationId));
    }

    private static string GetCorrelationId(HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
}
