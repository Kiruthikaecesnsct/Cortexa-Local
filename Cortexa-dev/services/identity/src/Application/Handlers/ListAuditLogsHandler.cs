using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;

namespace Cortexa.Identity.Application.Handlers;

public sealed class ListAuditLogsHandler
{
    private const int DefaultPage = 1;
    private const int DefaultSize = 20;
    private const int MaxSize = 100;

    private readonly IAuditLogRepository _auditLogRepository;

    public ListAuditLogsHandler(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PagedResult<AuditLogDto>> HandleAsync(ListAuditLogsRequest request, CancellationToken ct)
    {
        var page = ClampPage(request.Page);
        var size = ClampSize(request.Size);

        var filter = new AuditLogFilter(request.UserId, request.EventType, request.From, request.To);
        var result = await _auditLogRepository.QueryAsync(filter, page, size, ct);

        var items = result.Items
            .Select(a => new AuditLogDto(
                a.Id,
                a.UserId,
                a.EventType.ToString(),
                a.ResourceType,
                a.ResourceId,
                a.Action,
                a.Details,
                a.CreatedDate))
            .ToList();

        return new PagedResult<AuditLogDto>(items, page, size, result.Total);
    }

    private static int ClampPage(int page) => page < 1 ? DefaultPage : page;

    private static int ClampSize(int size)
    {
        if (size < 1)
            return DefaultSize;
        return size > MaxSize ? MaxSize : size;
    }
}
