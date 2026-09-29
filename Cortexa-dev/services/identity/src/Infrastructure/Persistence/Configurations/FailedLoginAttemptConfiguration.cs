using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class FailedLoginAttemptConfiguration : IEntityTypeConfiguration<FailedLoginAttempt>
{
    public void Configure(EntityTypeBuilder<FailedLoginAttempt> builder)
    {
        builder.ToTable("failed_login_attempts");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(f => f.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasIndex(f => f.UserId).IsUnique();

        builder.Property(f => f.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(f => f.LockoutCount)
            .HasColumnName("lockout_count")
            .IsRequired();

        builder.Property(f => f.WindowStartedAt)
            .HasColumnName("window_started_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired();

        builder.Property(f => f.LastAttemptAt)
            .HasColumnName("last_attempt_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired();

        builder.Property(f => f.LockedUntil)
            .HasColumnName("locked_until")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired(false);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
