using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cortexa.Identity.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbContextFactory<IdentityDbContext> _contextFactory;

    public AuditLogRepository(IDbContextFactory<IdentityDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.AuditLogs.Add(auditLog);
        await context.SaveChangesAsync(ct);
    }

    public async Task<AuditLogQueryResult> QueryAsync(
        AuditLogFilter filter,
        int page,
        int size,
        CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var query = context.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);

        if (filter.EventType is { } eventType)
            query = query.Where(a => a.EventType == eventType);

        if (filter.From is { } from)
            query = query.Where(a => a.CreatedDate >= from);

        if (filter.To is { } to)
            query = query.Where(a => a.CreatedDate <= to);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedDate)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new AuditLogQueryResult(items.AsReadOnly(), total);
    }
}
