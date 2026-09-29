using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Api.Endpoints;

public static class AdminAuditEndpoints
{
    public static WebApplication MapAdminAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/audit", ListAuditLogsAsync).RequireAuthorization("SuperAdminOnly");
        return app;
    }

    private static async Task<IResult> ListAuditLogsAsync(
        HttpRequest httpRequest,
        ListAuditLogsHandler handler,
        CancellationToken ct)
    {
        var request = ParseRequest(httpRequest.Query);
        var result = await handler.HandleAsync(request, ct);
        return ApiEnvelope.Success(result);
    }

    private static ListAuditLogsRequest ParseRequest(IQueryCollection query)
    {
        var userId = TryParseGuid(query["user_id"]);
        var eventType = TryParseEventType(query["event_type"]);
        var from = TryParseDate(query["from"]);
        var to = TryParseDate(query["to"]);
        var page = TryParseInt(query["page"]) ?? 1;
        var size = TryParseInt(query["size"]) ?? 20;

        return new ListAuditLogsRequest(userId, eventType, from, to, page, size);
    }

    private static Guid? TryParseGuid(string? raw)
        => Guid.TryParse(raw, out var value) ? value : null;

    private static AuditEventType? TryParseEventType(string? raw)
        => Enum.TryParse<AuditEventType>(raw, ignoreCase: true, out var value) ? value : null;

    private static DateTimeOffset? TryParseDate(string? raw)
        => DateTimeOffset.TryParse(raw, out var value) ? value : null;

    private static int? TryParseInt(string? raw)
        => int.TryParse(raw, out var value) ? value : null;
}
