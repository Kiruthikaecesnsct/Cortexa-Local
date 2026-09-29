using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasColumnType("VARCHAR(320)")
            .IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Username)
            .HasColumnName("username")
            .HasColumnType("VARCHAR(100)")
            .IsRequired();

        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasColumnType("TEXT")
            .IsRequired(false);

        builder.Property(u => u.EntraObjectId)
            .HasColumnName("entra_object_id")
            .HasColumnType("VARCHAR(100)")
            .IsRequired(false);

        builder.HasIndex(u => u.EntraObjectId)
            .IsUnique()
            .HasFilter("entra_object_id IS NOT NULL");

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasColumnType("VARCHAR(20)")
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(Role.Researcher);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_users_role",
            "role IN ('Researcher','Reviewer','Admin','SuperAdmin')"));

        builder.Property(u => u.IsSystem)
            .HasColumnName("is_system")
            .HasColumnType("BOOL")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(u => u.OrganizationId)
            .HasColumnName("org_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.HasIndex(u => u.OrganizationId);

        builder.Property(u => u.SecurityStamp)
            .HasColumnName("security_stamp")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
