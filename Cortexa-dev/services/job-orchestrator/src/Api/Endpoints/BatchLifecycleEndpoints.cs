using System.Security.Claims;
using Cortexa.JobOrchestrator.Api.Auth;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Application.Validation;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class BatchLifecycleEndpoints
{
    private const string SavedRepositoriesField = "saved_repositories";

    public static void MapBatchLifecycleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/batches").RequireAuthorization();

        group.MapPost("", HandleCreateBatch).DisableAntiforgery();
        group.MapPost("/start", HandleStartBatch);
        group.MapGet("", HandleListBatches);
        group.MapGet("/{id}/results", HandleGetResults);
        group.MapGet("/{id}/results/{candidateId}", HandleGetResultDetail);
        group.MapPost("/{id}/documents/{documentId}/retry", HandleRetryDocument);
        group.MapPost("/{id}/stop", HandleStopBatch);
        group.MapDelete("/{id}", HandleDeleteBatch).RequireAuthorization("BatchOperator");
    }

    private static async Task<IResult> HandleCreateBatch(
        IFormCollection form,
        CreateBatchHandler handler,
        IOptions<OrchestratorSettings> settings,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var files = form.Files;
        var repoUrl = form["repo_url"].ToString();
        var saved = SavedRepositoryParser.Parse(form[SavedRepositoriesField].ToArray(), settings.Value.MaxSavedRepositoriesPerBatch);

        if (saved.Error is not null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", saved.Error, correlationId));

        if (files.Count == 0 && string.IsNullOrWhiteSpace(repoUrl) && saved.Repositories.Count == 0)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "At least one file, a repository URL, or a saved repository is required.", correlationId));

        var fileInputs = files
            .Select(f => (f.FileName, f.OpenReadStream() as Stream, f.ContentType ?? "application/octet-stream"))
            .ToList();

        var command = new CreateBatchCommand(
            Files: fileInputs,
            BatchName: form["batch_name"].ToString(),
            Engine: form["engine"].ToString(),
            AiModel: form["ai_model"].ToString(),
            SeedCorpusDomain: form["seed_corpus_domain"].ToString(),
            RepoUrl: form["repo_url"].ToString() is { Length: > 0 } rUrl ? rUrl : null,
            GitProvider: form["git_provider"].ToString() is { Length: > 0 } gp ? gp : null,
            GitBranch: form["git_branch"].ToString() is { Length: > 0 } gb ? gb : null,
            GitPatRaw: form["git_pat"].ToString() is { Length: > 0 } pat ? pat : null,
            OrgId: ctx.Request.Headers["X-Org-Id"].ToString() is { Length: > 0 } orgId ? orgId : null,
            UserId: ctx.Request.Headers["X-User-Id"].ToString() is { Length: > 0 } userId ? userId : null,
            SavedRepositories: saved.Repositories);

        var result = await handler.HandleAsync(command, ct);
        return Results.Ok(ApiResponse<CreateBatchResponse>.Ok(result, correlationId));
    }

    private static async Task<IResult> HandleStartBatch(
        StartBatchHandler handler,
        HttpContext ctx,
        StartBatchRequest request,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);

        if (request is null || string.IsNullOrWhiteSpace(request.BatchId))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "batch_id is required.", correlationId));

        try
        {
            var result = await handler.HandleAsync(request.BatchId, ct);
            return Results.Ok(ApiResponse<StartBatchResponse>.Ok(result, correlationId));
        }
        catch (BatchNotFoundException)
        {
            return Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", $"Batch '{request.BatchId}' was not found.", correlationId));
        }
    }

    private static async Task<IResult> HandleListBatches(
        ListBatchesHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var result = await handler.HandleAsync(ct);
        return Results.Ok(ApiResponse<IReadOnlyList<BatchSummaryDto>>.Ok(result, correlationId));
    }

    private static async Task<IResult> HandleGetResults(
        string id,
        GetBatchResultsHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var result = await handler.HandleAsync(id, ct);
        return Results.Ok(ApiResponse<BatchResultsResponse>.Ok(result, correlationId));
    }

    private static async Task<IResult> HandleGetResultDetail(
        string id,
        string candidateId,
        GetResultDetailHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var result = await handler.HandleAsync(id, candidateId, ct);

        if (result is null)
            return Results.NotFound(ApiResponse<object>.Fail("CANDIDATE_NOT_FOUND", $"Candidate '{candidateId}' was not found.", correlationId));

        return Results.Ok(ApiResponse<CandidateResultDto>.Ok(result, correlationId));
    }

    private static async Task<IResult> HandleRetryDocument(
        string id,
        string documentId,
        RetryDocumentHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);

        try
        {
            await handler.HandleAsync(id, documentId, ct);
            return Results.Ok(ApiResponse<object>.Ok(new { }, correlationId));
        }
        catch (BatchNotFoundException)
        {
            return Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", $"Batch '{id}' was not found.", correlationId));
        }
        catch (DocumentNotFoundException)
        {
            return Results.NotFound(ApiResponse<object>.Fail("DOCUMENT_NOT_FOUND", $"Document '{documentId}' was not found.", correlationId));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("INVALID_STATE", ex.Message, correlationId));
        }
    }

    private static async Task<IResult> HandleStopBatch(
        string id,
        StopBatchRequest request,
        CancelBatchHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);

        try
        {
            var result = await handler.HandleAsync(id, request.Reason, ct);
            return Results.Ok(ApiResponse<StopBatchResponse>.Ok(result, correlationId));
        }
        catch (BatchNotFoundException)
        {
            return Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", $"Batch '{id}' was not found.", correlationId));
        }
        catch (ConcurrencyConflictException)
        {
            return Results.Conflict(ApiResponse<object>.Fail("CONCURRENCY_CONFLICT", "The batch was modified concurrently. Please retry.", correlationId));
        }
    }

    private static async Task<IResult> HandleDeleteBatch(
        string id,
        bool? force,
        DeleteBatchHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var callerResult = CallerContextResolver.Resolve(ctx);

        if (!callerResult.IsSuccess)
            return Results.BadRequest(ApiResponse<object>.Fail("MISSING_ORG_CONTEXT", callerResult.Error!, correlationId));

        var caller = callerResult.Context!;
        var access = new BatchAccess(caller.OrgId, caller.IsSuperAdmin);
        var options = new DeletionRequestOptions(caller.UserId, correlationId, force ?? false);
        var context = new DeleteBatchContext(id, options, access);

        return await ExecuteDeleteAsync(handler, context, correlationId, ct);
    }

    private static async Task<IResult> ExecuteDeleteAsync(
        DeleteBatchHandler handler,
        DeleteBatchContext context,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var result = await handler.HandleAsync(context, ct);
            return Results.Ok(ApiResponse<DeleteBatchResponse>.Ok(result, correlationId));
        }
        catch (Exception ex) when (ex is InvalidBatchStateException or BatchNotFoundException or CrossOrgAccessException)
        {
            return MapDeleteException(ex, correlationId);
        }
    }

    public static IResult MapDeleteException(Exception ex, string correlationId) => ex switch
    {
        InvalidBatchStateException => Results.BadRequest(ApiResponse<object>.Fail("INVALID_STATE", ex.Message, correlationId)),
        BatchNotFoundException => Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", ex.Message, correlationId)),
        CrossOrgAccessException => Results.Json(
            ApiResponse<object>.Fail("CROSS_ORG_ACCESS_DENIED", ex.Message, correlationId),
            statusCode: StatusCodes.Status403Forbidden),
        _ => throw ex
    };

    private static string GetOperator(HttpContext ctx) =>
        ctx.User.Identity?.Name
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "unknown";

    private static string GetCorrelationId(HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
}

public sealed record StartBatchRequest(string BatchId);
