using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Validation;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class PatentSecretsEndpoints
{
    public static void MapPatentSecretsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/patent-config/secrets").RequireAuthorization("BatchOperator");

        group.MapPost("/{source}", HandleWriteSecret);
        group.MapPost("/{source}/test", HandleTestConnection);
    }

    private static async Task<IResult> HandleWriteSecret(
        string source,
        PatentSecretRequest request,
        IPatentSourceConfigRepository repository,
        IPatentSecretWriter secretWriter,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = PatentConfigEndpoints.GetCorrelationId(ctx);

        if (!PatentSourceValidator.TryParseSource(source, out var parsedSource))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Unknown patent source.", correlationId));

        if (request is null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Request body is required.", correlationId));

        var validationError = PatentSourceValidator.ValidateSecretWrite(parsedSource, request.ApiKey, request.ConsumerKey, request.OauthSecret);
        if (validationError is not null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", validationError, correlationId));

        try
        {
            await WriteSecretsAsync(parsedSource, request, secretWriter, ct);
        }
        catch (InvalidOperationException)
        {
            return Results.Json(
                ApiResponse<object>.Fail("SECRET_STORE_UNAVAILABLE", "Unable to store the credential. Please try again.", correlationId),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var current = await repository.GetAsync(ct);
        var saved = await repository.UpdateAsync(current with { UpdatedBy = PatentConfigEndpoints.GetOperator(ctx) }, ct);
        var response = await PatentConfigEndpoints.BuildResponseAsync(saved, secretWriter, ct);

        return Results.Ok(ApiResponse<PatentConfigResponse>.Ok(response, correlationId));
    }

    private static async Task WriteSecretsAsync(
        PatentSource source,
        PatentSecretRequest request,
        IPatentSecretWriter secretWriter,
        CancellationToken ct)
    {
        if (source == PatentSource.Epo)
        {
            await secretWriter.SetSecretAsync(PatentSecretNames.EpoConsumerKey, request.ConsumerKey!.Trim(), ct);
            await secretWriter.SetSecretAsync(PatentSecretNames.EpoOAuthSecret, request.OauthSecret!.Trim(), ct);
            return;
        }

        var secretName = source == PatentSource.Uspto ? PatentSecretNames.UsptoApiKey : PatentSecretNames.LensApiKey;
        await secretWriter.SetSecretAsync(secretName, request.ApiKey!.Trim(), ct);
    }

    private static async Task<IResult> HandleTestConnection(
        string source,
        PatentSecretRequest? request,
        IPatentConnectionProbe probe,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = PatentConfigEndpoints.GetCorrelationId(ctx);

        if (!PatentSourceValidator.TryParseSource(source, out var parsedSource))
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Unknown patent source.", correlationId));

        var result = await RunProbeAsync(parsedSource, request, probe, ct);
        if (result.IsValidationError)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", result.ValidationError!, correlationId));

        var response = new PatentProbeResponse(result.ProbeResult!.Success, result.ProbeResult.FailureReason);
        return Results.Ok(ApiResponse<PatentProbeResponse>.Ok(response, correlationId));
    }

    private static async Task<ProbeOutcome> RunProbeAsync(
        PatentSource source,
        PatentSecretRequest? request,
        IPatentConnectionProbe probe,
        CancellationToken ct)
    {
        if (!HasSuppliedCredential(request))
            return ProbeOutcome.FromResult(await probe.TestStoredAsync(source, ct));

        var validationError = PatentSourceValidator.ValidateSecretWrite(source, request!.ApiKey, request.ConsumerKey, request.OauthSecret);
        if (validationError is not null)
            return ProbeOutcome.FromValidationError(validationError);

        var material = new PatentSecretMaterial(request.ApiKey?.Trim(), request.ConsumerKey?.Trim(), request.OauthSecret?.Trim());
        return ProbeOutcome.FromResult(await probe.TestSuppliedAsync(source, material, ct));
    }

    private static bool HasSuppliedCredential(PatentSecretRequest? request) =>
        request is not null
        && (!string.IsNullOrWhiteSpace(request.ApiKey)
            || !string.IsNullOrWhiteSpace(request.ConsumerKey)
            || !string.IsNullOrWhiteSpace(request.OauthSecret));

    private sealed record ProbeOutcome(PatentProbeResult? ProbeResult, string? ValidationError)
    {
        public bool IsValidationError => ValidationError is not null;

        public static ProbeOutcome FromResult(PatentProbeResult result) => new(result, null);

        public static ProbeOutcome FromValidationError(string error) => new(null, error);
    }
}

public sealed record PatentSecretRequest(string? ApiKey, string? ConsumerKey, string? OauthSecret);

public sealed record PatentProbeResponse(bool Success, string? FailureReason);
