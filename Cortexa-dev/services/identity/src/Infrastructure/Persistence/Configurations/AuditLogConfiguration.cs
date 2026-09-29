using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid");

        builder.Property(a => a.EventType)
            .HasColumnName("event_type")
            .HasColumnType("TEXT")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.ResourceType)
            .HasColumnName("resource_type")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.ResourceId)
            .HasColumnName("resource_id")
            .HasColumnType("TEXT");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(a => a.Details)
            .HasColumnName("details")
            .HasColumnType("TEXT");

        builder.Property(a => a.CreatedDate)
            .HasColumnName("created_date")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_audit_logs_user");

        builder.HasIndex(a => a.EventType)
            .HasDatabaseName("ix_audit_logs_event_type");

        builder.HasIndex(a => a.CreatedDate)
            .HasDatabaseName("ix_audit_logs_created_date");
    }
}
