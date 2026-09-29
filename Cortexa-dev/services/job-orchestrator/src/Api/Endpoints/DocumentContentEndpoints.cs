using Cortexa.JobOrchestrator.Api.Auth;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class DocumentContentEndpoints
{
    public static void MapDocumentContentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/documents").RequireAuthorization();

        group.MapGet("/{batchId}/{documentId}/content", HandleGetDocumentContent);
    }

    private static async Task<IResult> HandleGetDocumentContent(
        string batchId,
        string documentId,
        GetDocumentHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var callerResult = CallerContextResolver.Resolve(ctx);

        if (!callerResult.IsSuccess)
            return Results.BadRequest(ApiResponse<object>.Fail("MISSING_ORG_CONTEXT", callerResult.Error!, correlationId));

        var caller = callerResult.Context!;
        var access = new BatchAccess(caller.OrgId, caller.IsSuperAdmin);

        return await ExecuteAsync(handler, batchId, documentId, access, correlationId, ct);
    }

    private static async Task<IResult> ExecuteAsync(
        GetDocumentHandler handler,
        string batchId,
        string documentId,
        BatchAccess access,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var result = await handler.HandleAsync(batchId, documentId, access, ct);
            return Results.Stream(result.Content, result.ContentType, result.FileName);
        }
        catch (Exception ex) when (ex is CrossOrgAccessException or BatchNotFoundException
            or DocumentNotFoundException or ViewableArtifactNotFoundException)
        {
            return MapException(ex, correlationId);
        }
    }

    private static IResult MapException(Exception ex, string correlationId) => ex switch
    {
        CrossOrgAccessException => Results.Json(
            ApiResponse<object>.Fail("CROSS_ORG_ACCESS_DENIED", ex.Message, correlationId),
            statusCode: StatusCodes.Status403Forbidden),
        BatchNotFoundException => Results.NotFound(ApiResponse<object>.Fail("BATCH_NOT_FOUND", ex.Message, correlationId)),
        DocumentNotFoundException => Results.NotFound(ApiResponse<object>.Fail("DOCUMENT_NOT_FOUND", ex.Message, correlationId)),
        ViewableArtifactNotFoundException => Results.NotFound(ApiResponse<object>.Fail("NO_VIEWABLE_ARTIFACT", ex.Message, correlationId)),
        _ => throw ex
    };

    private static string GetCorrelationId(HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
}
