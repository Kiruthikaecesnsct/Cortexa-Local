using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Entities;

public sealed class AuditLogTests
{
    [Fact]
    public void Create_SetsAllPropertiesAndGeneratesId()
    {
        var actorId = Guid.NewGuid();

        var auditLog = AuditLog.Create(
            actorId, AuditEventType.UserCreated, "User", "resource-1", "create", "{\"role\":\"Admin\"}");

        Assert.NotEqual(Guid.Empty, auditLog.Id);
        Assert.Equal(actorId, auditLog.UserId);
        Assert.Equal(AuditEventType.UserCreated, auditLog.EventType);
        Assert.Equal("User", auditLog.ResourceType);
        Assert.Equal("resource-1", auditLog.ResourceId);
        Assert.Equal("create", auditLog.Action);
        Assert.Equal("{\"role\":\"Admin\"}", auditLog.Details);
        Assert.True(auditLog.CreatedDate <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_WithNullActor_AllowsAnonymousAttribution()
    {
        var auditLog = AuditLog.Create(
            null, AuditEventType.LoginFailed, "Auth", null, "login", null);

        Assert.Null(auditLog.UserId);
        Assert.Null(auditLog.ResourceId);
        Assert.Null(auditLog.Details);
    }

    [Fact]
    public void AuditLog_HasNoPublicMutators()
    {
        var publicMethods = typeof(AuditLog)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AuditLog))
            .ToList();

        Assert.Empty(publicMethods);
    }

    [Fact]
    public async Task SaveChanges_ModifyingExistingAuditLog_ThrowsInvalidOperationException()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditImmutabilityInterceptor())
            .Options;

        await using (var setupContext = new IdentityDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var auditLog = AuditLog.Create(Guid.NewGuid(), AuditEventType.UserCreated, "User", "r1", "create", null);
        await using (var writeContext = new IdentityDbContext(options))
        {
            writeContext.AuditLogs.Add(auditLog);
            await writeContext.SaveChangesAsync();
        }

        await using var mutateContext = new IdentityDbContext(options);
        var tracked = await mutateContext.AuditLogs.FirstAsync(a => a.Id == auditLog.Id);
        mutateContext.Entry(tracked).Property("Action").CurrentValue = "tampered";
        mutateContext.Entry(tracked).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(() => mutateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_DeletingExistingAuditLog_ThrowsInvalidOperationException()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditImmutabilityInterceptor())
            .Options;

        await using (var setupContext = new IdentityDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var auditLog = AuditLog.Create(Guid.NewGuid(), AuditEventType.UserCreated, "User", "r1", "create", null);
        await using (var writeContext = new IdentityDbContext(options))
        {
            writeContext.AuditLogs.Add(auditLog);
            await writeContext.SaveChangesAsync();
        }

        await using var deleteContext = new IdentityDbContext(options);
        var tracked = await deleteContext.AuditLogs.FirstAsync(a => a.Id == auditLog.Id);
        deleteContext.AuditLogs.Remove(tracked);

        await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_InsertingNewAuditLog_Succeeds()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditImmutabilityInterceptor())
            .Options;

        await using (var setupContext = new IdentityDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var auditLog = AuditLog.Create(Guid.NewGuid(), AuditEventType.UserCreated, "User", "r1", "create", null);
        await using var writeContext = new IdentityDbContext(options);
        writeContext.AuditLogs.Add(auditLog);

        var exception = await Record.ExceptionAsync(() => writeContext.SaveChangesAsync());

        Assert.Null(exception);
    }
}
