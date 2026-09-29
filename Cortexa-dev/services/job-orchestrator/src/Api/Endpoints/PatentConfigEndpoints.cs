using System.Security.Claims;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Api.Endpoints;

public static class PatentConfigEndpoints
{
    public static void MapPatentConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/patent-config").RequireAuthorization("BatchOperator");

        group.MapGet("", HandleGetPatentConfig);
        group.MapPut("", HandlePutPatentConfig);
    }

    private static async Task<IResult> HandleGetPatentConfig(
        IPatentSourceConfigRepository repository,
        IPatentSecretWriter secretWriter,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);
        var config = await repository.GetAsync(ct);
        var response = await BuildResponseAsync(config, secretWriter, ct);

        return Results.Ok(ApiResponse<PatentConfigResponse>.Ok(response, correlationId));
    }

    private static async Task<IResult> HandlePutPatentConfig(
        PatentConfigRequest request,
        IPatentSourceConfigRepository repository,
        IPatentSecretWriter secretWriter,
        HttpContext ctx,
        CancellationToken ct)
    {
        var correlationId = GetCorrelationId(ctx);

        if (request is null)
            return Results.BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Request body is required.", correlationId));

        var current = await repository.GetAsync(ct);
        var desired = current
            .WithFlag(PatentSource.Uspto, request.UsptoEnabled)
            .WithFlag(PatentSource.Epo, request.EpoEnabled)
            .WithFlag(PatentSource.Lens, request.LensEnabled) with
        {
            UpdatedBy = GetOperator(ctx)
        };

        var saved = await repository.UpdateAsync(desired, ct);
        var response = await BuildResponseAsync(saved, secretWriter, ct);

        return Results.Ok(ApiResponse<PatentConfigResponse>.Ok(response, correlationId));
    }

    internal static async Task<PatentConfigResponse> BuildResponseAsync(
        PatentSourceConfig config,
        IPatentSecretWriter secretWriter,
        CancellationToken ct)
    {
        var usptoStatus = PatentCredentialStatus.From(await secretWriter.SecretExistsAsync(PatentSecretNames.UsptoApiKey, ct));
        var lensStatus = PatentCredentialStatus.From(await secretWriter.SecretExistsAsync(PatentSecretNames.LensApiKey, ct));
        var epoConsumerStatus = PatentCredentialStatus.From(await secretWriter.SecretExistsAsync(PatentSecretNames.EpoConsumerKey, ct));
        var epoOauthStatus = PatentCredentialStatus.From(await secretWriter.SecretExistsAsync(PatentSecretNames.EpoOAuthSecret, ct));

        return new PatentConfigResponse(
            config.Uspto.Enabled,
            config.Epo.Enabled,
            config.Lens.Enabled,
            usptoStatus,
            new EpoCredentialStatus(epoConsumerStatus, epoOauthStatus),
            lensStatus,
            config.ConfigVersion,
            config.UpdatedAt,
            config.UpdatedBy);
    }

    internal static string GetOperator(HttpContext ctx) =>
        ctx.User.Identity?.Name
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "unknown";

    internal static string GetCorrelationId(HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
}

public sealed record PatentConfigRequest(bool UsptoEnabled, bool EpoEnabled, bool LensEnabled);

public sealed record EpoCredentialStatus(string Consumer, string Oauth);

public sealed record PatentConfigResponse(
    bool UsptoEnabled,
    bool EpoEnabled,
    bool LensEnabled,
    string UsptoCredentialStatus,
    EpoCredentialStatus EpoCredentialStatus,
    string LensCredentialStatus,
    int ConfigVersion,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);
