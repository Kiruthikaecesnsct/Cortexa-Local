using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(rt => rt.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(rt => rt.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(rt => rt.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired();

        builder.Property(rt => rt.Revoked)
            .HasColumnName("revoked")
            .HasColumnType("BOOL")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(rt => rt.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("TIMESTAMPTZ");

        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasIndex(rt => rt.UserId)
            .HasDatabaseName("ix_refresh_tokens_user");

        builder.HasIndex(rt => rt.ExpiresAt)
            .HasDatabaseName("ix_refresh_tokens_expiry");
    }
}
