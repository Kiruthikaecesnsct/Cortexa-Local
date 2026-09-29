using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Services;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/config").RequireAuthorization();

        group.MapGet("", HandleGetConfig);
        group.MapPut("", HandlePutConfig).RequireAuthorization("BatchOperator");
    }

    private static async Task<IResult> HandleGetConfig(
        IConfigRepository repository,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var config = await repository.GetAsync(ct);

        var response = new ConfigResponse(
            config.ExtractionModel,
            config.PrimaryEvidenceModel,
            config.ScoringModel,
            config.SeedingModel,
            config.SeedingMode,
            config.UpdatedAt);

        return Results.Ok(ApiResponse<ConfigResponse>.Ok(response, correlationId));
    }

    private static async Task<IResult> HandlePutConfig(
        ConfigRequest request,
        IConfigRepository repository,
        ModelConfigValidator validator,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);

        if (request is null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Request body is required.", correlationId));

        if (string.IsNullOrWhiteSpace(request.ExtractionModel))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Extraction_Model is required.", correlationId));

        if (string.IsNullOrWhiteSpace(request.PrimaryEvidenceModel))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Primary_Evidence_Model is required.", correlationId));

        if (string.IsNullOrWhiteSpace(request.ScoringModel))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Scoring_Model is required.", correlationId));

        if (string.IsNullOrWhiteSpace(request.SeedingModel))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Seeding_Model is required.", correlationId));

        var seedingMode = string.IsNullOrWhiteSpace(request.SeedingMode)
            ? SeedingModes.Legacy
            : request.SeedingMode;

        var seedingModeError = ModelConfigValidator.ValidateSeedingMode(seedingMode);
        if (seedingModeError is not null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", seedingModeError, correlationId));

        var validationResult = await validator.ValidateAsync(
            new StageModelSelection(request.ExtractionModel, request.PrimaryEvidenceModel, request.ScoringModel, request.SeedingModel),
            ct);

        if (validationResult.IsCatalogUnavailable)
        {
            return Results.Json(
                ApiResponse<object>.Fail("CATALOG_UNAVAILABLE", validationResult.BuildErrorMessage(), correlationId),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!validationResult.IsValid)
        {
            return Results.BadRequest(
                ApiResponse<object>.Fail("INVALID_MODEL_FOR_STAGE", validationResult.BuildErrorMessage(), correlationId));
        }

        var config = new ModelConfig(
            request.ExtractionModel,
            request.PrimaryEvidenceModel,
            request.ScoringModel,
            request.SeedingModel,
            SeedingModes.Normalize(seedingMode),
            DateTimeOffset.UtcNow);

        await repository.UpdateAsync(config, ct);

        var response = new ConfigResponse(
            config.ExtractionModel,
            config.PrimaryEvidenceModel,
            config.ScoringModel,
            config.SeedingModel,
            config.SeedingMode,
            config.UpdatedAt);

        return Results.Ok(ApiResponse<ConfigResponse>.Ok(response, correlationId));
    }

    private static string GetCorrelationId(HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
}

public sealed record ConfigRequest(
    string ExtractionModel,
    string PrimaryEvidenceModel,
    string ScoringModel,
    string SeedingModel,
    string? SeedingMode = null);

public sealed record ConfigResponse(
    string ExtractionModel,
    string PrimaryEvidenceModel,
    string ScoringModel,
    string SeedingModel,
    string SeedingMode,
    DateTimeOffset UpdatedAt);
