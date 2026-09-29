using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rp => new { rp.Role, rp.PermissionId });

        builder.Property(rp => rp.Role)
            .HasColumnName("role")
            .HasColumnType("VARCHAR(20)")
            .HasConversion<string>()
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_role_permissions_role",
            "role IN ('Researcher','Reviewer','Admin','SuperAdmin')"));

        builder.Property(rp => rp.PermissionId)
            .HasColumnName("permission_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne(rp => rp.Permission)
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
