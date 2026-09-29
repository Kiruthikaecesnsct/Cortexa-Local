using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cortexa.Identity.Infrastructure.Persistence;

public sealed class AuditImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureNoMutations(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureNoMutations(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void EnsureNoMutations(DbContext? context)
    {
        if (context is null)
            return;

        var hasMutation = context.ChangeTracker.Entries<AuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (hasMutation)
            throw new InvalidOperationException("Audit log entries are append-only and cannot be modified or deleted.");
    }
}
