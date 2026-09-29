using Cortexa.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cortexa.Identity.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasColumnType("VARCHAR(100)")
            .IsRequired();

        builder.HasIndex(p => p.Name).IsUnique();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(p => p.CreatedDate)
            .HasColumnName("created_date")
            .HasColumnType("TIMESTAMPTZ")
            .IsRequired()
            .HasDefaultValueSql("now()");
    }
}
